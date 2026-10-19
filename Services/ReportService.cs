using Grafana_Monitor.Data;
using Grafana_Monitor.Models;
using Grafana_Monitor.Options;
using Microsoft.Extensions.Options;

namespace Grafana_Monitor.Services;

public sealed class ReportService(IReportRepository repository, IOptions<DemoOptions> options)
{
    public async Task<HeavyReportDto> GetHeavyReportAsync(CancellationToken cancellationToken)
    {
        var config = options.Value;
        var count = await repository.GetOrderCountAsync(
            "heavy",
            config.HeavySleepSeconds,
            config.ReportMode == "slow",
            cancellationToken);
        return new HeavyReportDto(count, config.ReportMode);
    }

    // Report "normal": query COUNT(*) yang sama tanpa pg_sleep — pembanding cepat untuk demo
    // (label endpoint="normal" membuat panel SQL execution p95 memisahkan normal vs heavy).
    public async Task<HeavyReportDto> GetNormalReportAsync(CancellationToken cancellationToken)
    {
        var count = await repository.GetOrderCountAsync("normal", 0, simulateSlowQuery: false, cancellationToken);
        return new HeavyReportDto(count, "normal");
    }
}
