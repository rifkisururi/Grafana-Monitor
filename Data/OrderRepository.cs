using System.Diagnostics;
using Grafana_Monitor.Metrics;
using Grafana_Monitor.Models;
using Npgsql;

namespace Grafana_Monitor.Data;

public sealed class OrderRepository(NpgsqlDataSource dataSource, DbPoolMetrics metrics) : IOrderRepository
{
    public async Task<IReadOnlyList<OrderDto>> GetOrdersAsync(CancellationToken cancellationToken)
    {
        await using var lease = await metrics.AcquireAsync(dataSource, "orders", cancellationToken);
        var orders = new List<OrderDto>();
        var timer = Stopwatch.StartNew();
        await using (var command = new NpgsqlCommand(
            "SELECT id, product_id, quantity, created_at FROM orders ORDER BY id DESC LIMIT 20", lease.Connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                orders.Add(new OrderDto(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2), reader.GetDateTime(3)));
            }
        }
        metrics.RecordQuery("orders", timer.Elapsed.TotalSeconds);
        return orders;
    }
}
