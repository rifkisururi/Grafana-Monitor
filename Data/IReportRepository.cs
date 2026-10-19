namespace Grafana_Monitor.Data;

public interface IReportRepository
{
    Task<long> GetOrderCountAsync(string endpoint, int sleepSeconds, bool simulateSlowQuery, CancellationToken cancellationToken);
}
