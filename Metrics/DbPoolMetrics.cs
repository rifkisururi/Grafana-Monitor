using System.Diagnostics;
using System.Diagnostics.Metrics;
using Npgsql;

namespace Grafana_Monitor.Metrics;

// Metrik custom untuk demo pool contention. Meter "Demo.Api" didaftarkan di Program.cs
// lewat AddMeter(MeterName), sehingga semua instrumen di kelas ini otomatis muncul di /metrics
// dan di-scrape Prometheus. Nama histogram harus sama dengan nama pada AddView() di Program.cs
// supaya bucket-nya diterapkan.
public sealed class DbPoolMetrics
{
    private static readonly Meter Meter = new("Demo.Api", "1.0.0");

    // Durasi menunggu koneksi tersedia dari pool (detik, label: endpoint).
    private static readonly Histogram<double> AcquireDuration = Meter.CreateHistogram<double>("demo_db_pool_acquire_seconds", "s");

    // Durasi eksekusi query (detik, label: endpoint).
    private static readonly Histogram<double> QueryDuration = Meter.CreateHistogram<double>("demo_db_query_seconds", "s");
    private long _waiters;
    private long _leased;

    public DbPoolMetrics()
    {
        // Jumlah request yang sedang menunggu koneksi — naik tajam saat pool penuh.
        Meter.CreateObservableGauge("demo_db_pool_waiters", () => Interlocked.Read(ref _waiters), "{request}");

        // Jumlah koneksi yang sedang dipakai — bandingkan dengan Maximum Pool Size (DB_POOL_MAX_SIZE).
        Meter.CreateObservableGauge("demo_db_connection_leased", () => Interlocked.Read(ref _leased), "{connection}");
    }

    public async Task<ConnectionLease> AcquireAsync(NpgsqlDataSource dataSource, string endpoint, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _waiters);
        var timer = Stopwatch.StartNew();
        NpgsqlConnection connection;
        try
        {
            connection = await dataSource.OpenConnectionAsync(cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref _waiters);
            AcquireDuration.Record(timer.Elapsed.TotalSeconds, new TagList { { "endpoint", endpoint } });
        }

        Interlocked.Increment(ref _leased);
        return new ConnectionLease(connection, () => Interlocked.Decrement(ref _leased));
    }

    public void RecordQuery(string endpoint, double seconds) =>
        QueryDuration.Record(seconds, new TagList { { "endpoint", endpoint } });

    public sealed class ConnectionLease(NpgsqlConnection connection, Action onDispose) : IAsyncDisposable
    {
        public NpgsqlConnection Connection { get; } = connection;

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Connection.DisposeAsync();
            }
            finally
            {
                onDispose();
            }
        }
    }
}
