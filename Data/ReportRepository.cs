using System.Diagnostics;
using Grafana_Monitor.Metrics;
using Npgsql;

namespace Grafana_Monitor.Data;

public sealed class ReportRepository(NpgsqlDataSource dataSource, DbPoolMetrics metrics) : IReportRepository
{
    public async Task<long> GetOrderCountAsync(string endpoint, int sleepSeconds, bool simulateSlowQuery, CancellationToken cancellationToken)
    {
        await using var lease = await metrics.AcquireAsync(dataSource, endpoint, cancellationToken);
        var timer = Stopwatch.StartNew();
        if (simulateSlowQuery)
        {
            await using var sleep = new NpgsqlCommand("SELECT pg_sleep(@seconds)", lease.Connection);
            sleep.Parameters.AddWithValue("seconds", sleepSeconds);
            await sleep.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var count = new NpgsqlCommand("SELECT COUNT(*) FROM orders", lease.Connection);
        var orderCount = (long)(await count.ExecuteScalarAsync(cancellationToken) ?? 0L);
        metrics.RecordQuery(endpoint, timer.Elapsed.TotalSeconds);
        return orderCount;
    }
}
