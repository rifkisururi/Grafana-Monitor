using Grafana_Monitor.Data;
using Grafana_Monitor.Metrics;
using Grafana_Monitor.Options;
using Grafana_Monitor.Services;
using Microsoft.Extensions.Options;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
// CORS terbuka (khusus demo lokal, tanpa autentikasi): memungkinkan wwwroot/stress.html
// menembak API ini dari file:// atau origin lain tanpa diblokir browser.
builder.Services.AddCors(options => options.AddDefaultPolicy(
    policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddOptions<DemoOptions>()
    .Bind(builder.Configuration.GetSection(DemoOptions.SectionName))
    .PostConfigure<IConfiguration>((options, configuration) =>
    {
        if (configuration["DEMO_HEAVY_SLEEP_SECONDS"] is { } sleepSeconds)
        {
            options.HeavySleepSeconds = int.TryParse(sleepSeconds, out var parsedSleep) ? parsedSleep : -1;
        }

        if (configuration["DEMO_REPORT_MODE"] is { } reportMode)
        {
            options.ReportMode = reportMode;
        }

        ApplyPositiveIntOverride(configuration, "DEMO_STRESS_MEMORY_MEGABYTES", "Demo:StressMemoryMegabytes", value => options.StressMemoryMegabytes = value);
        ApplyPositiveIntOverride(configuration, "DEMO_STRESS_DURATION_SECONDS", "Demo:StressDurationSeconds", value => options.StressDurationSeconds = value);
        ApplyPositiveIntOverride(configuration, "DEMO_STRESS_MAX_CONCURRENT_REQUESTS", "Demo:StressMaxConcurrentRequests", value => options.StressMaxConcurrentRequests = value);
    })
    .Validate(options => options.HeavySleepSeconds is >= 0 and <= 5,
        "DEMO_HEAVY_SLEEP_SECONDS harus berupa bilangan bulat 0–5.")
    .Validate(options => options.ReportMode is "slow" or "fast",
        "DEMO_REPORT_MODE harus slow atau fast.")
    .Validate(options => options.StressMemoryMegabytes is >= 1 and <= 256,
        "Demo:StressMemoryMegabytes harus antara 1–256 MB.")
    .Validate(options => options.StressDurationSeconds is >= 1 and <= 30,
        "Demo:StressCpuSeconds harus antara 1–30 detik.")
    .Validate(options => options.StressMaxConcurrentRequests is >= 1 and <= 4,
        "Demo:StressMaxConcurrentRequests harus antara 1–4.")
    .ValidateOnStart();

var databaseUrl = builder.Configuration["DATABASE_URL"];
var connectionString = !string.IsNullOrWhiteSpace(databaseUrl)
    ? PostgresUrlParser.ToNpgsqlConnectionString(databaseUrl)
    : builder.Configuration.GetConnectionString("AppDb");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Konfigurasikan DATABASE_URL (Neon) atau ConnectionStrings:AppDb (format Npgsql).");
}
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString) { Name = "DemoDbPool" };
builder.Services.AddSingleton(dataSourceBuilder.Build());
builder.Services.AddSingleton<DbPoolMetrics>();
builder.Services.AddSingleton<IProductRepository, ProductRepository>();
builder.Services.AddSingleton<IOrderRepository, OrderRepository>();
builder.Services.AddSingleton<IReportRepository, ReportRepository>();
builder.Services.AddScoped<ProductService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddSingleton<ResourceStressGate>(serviceProvider =>
    new ResourceStressGate(serviceProvider.GetRequiredService<IOptions<DemoOptions>>().Value.StressMaxConcurrentRequests));
builder.Services.AddScoped<ResourceStressService>();

// ===== OpenTelemetry Metrics → Prometheus =====
// Metrik TIDAK didorong (push) ke Prometheus. Aplikasi hanya mengekspos endpoint /metrics,
// lalu Prometheus menariknya (pull/scrape) sesuai scrape_interval di prometheus/prometheus.yml.
builder.Services.AddOpenTelemetry()
    // Identitas aplikasi; muncul sebagai label service_name pada semua metrik yang diekspor.
    .ConfigureResource(resource => resource.AddService("log-monitor-demo-api"))
    .WithMetrics(metrics => metrics
        // Daftar meter yang metriknya ikut diekspor ke /metrics:
        // - Microsoft.AspNetCore.Hosting : metrik bawaan ASP.NET Core (request duration, error, dll.)
        // - Npgsql                       : metrik pool koneksi database dari driver Npgsql
        // - MeterName ("Demo.Api")       : meter custom DbPoolMetrics untuk demo pool contention
        .AddMeter("Microsoft.AspNetCore.Hosting", "Npgsql", "System.Runtime", ProgramConstants.MeterName)
        // Histogram perlu "view" dengan bucket eksplisit agar granularity histogram di Prometheus/Grafana
        // sesuai kebutuhan demo (bucket default OpenTelemetry terlalu halus untuk latensi dalam detik).
        .AddView("demo_db_pool_acquire_seconds", new ExplicitBucketHistogramConfiguration
        {
            Boundaries = [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2, 3, 5, 10, 20]
        })
        .AddView("demo_db_query_seconds", new ExplicitBucketHistogramConfiguration
        {
            Boundaries = [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2, 3, 5, 10, 20]
        })
        // Exporter Prometheus: menyiapkan data dalam format exposition Prometheus
        // agar bisa dibaca dari endpoint /metrics (dipetakan di bawah).
        .AddPrometheusExporter());

var app = builder.Build();
app.UseCors(); // harus sebelum Map* agar preflight OPTIONS terlayani
app.UseStaticFiles(); // sajikan wwwroot/ → http://<host>:8080/stress.html
app.MapControllers();
app.MapOpenApi();
app.MapScalarApiReference();
// Endpoint scrape Prometheus (path default: /metrics) — inilah target job "aspnet-api"
// di prometheus/prometheus.yml. Cek isiannya di http://localhost:8080/metrics.
app.MapPrometheusScrapingEndpoint().DisableHttpMetrics(); // request scrape itu sendiri tidak dihitung sebagai metrik HTTP
app.Run();

static void ApplyPositiveIntOverride(
    IConfiguration configuration,
    string environmentKey,
    string configurationKey,
    Action<int> apply)
{
    var value = configuration[environmentKey] ?? configuration[configurationKey];
    if (value is not null)
    {
        apply(int.TryParse(value, out var parsed) ? parsed : -1);
    }
}

static class ProgramConstants
{
    public const string MeterName = "Demo.Api";
}
