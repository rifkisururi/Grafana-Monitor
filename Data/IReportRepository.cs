namespace Grafana_Monitor.Data;

public interface IReportRepository
{
    Task<long> GetOrderCountAsync(int sleepSeconds, bool simulateSlowQuery, CancellationToken cancellationToken);
}
