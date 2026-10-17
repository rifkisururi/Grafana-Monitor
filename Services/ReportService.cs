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
            config.HeavySleepSeconds,
            config.ReportMode == "slow",
            cancellationToken);
        return new HeavyReportDto(count, config.ReportMode);
    }
}
