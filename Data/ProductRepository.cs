using System.Diagnostics;
using Grafana_Monitor.Metrics;
using Grafana_Monitor.Models;
using Npgsql;

namespace Grafana_Monitor.Data;

public sealed class ProductRepository(NpgsqlDataSource dataSource, DbPoolMetrics metrics) : IProductRepository
{
    public async Task<IReadOnlyList<ProductDto>> GetProductsAsync(CancellationToken cancellationToken)
    {
        await using var lease = await metrics.AcquireAsync(dataSource, "products", cancellationToken);
        var products = new List<ProductDto>();
        var timer = Stopwatch.StartNew();
        await using (var command = new NpgsqlCommand(
            "SELECT id, name, price FROM products ORDER BY id LIMIT 20", lease.Connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                products.Add(new ProductDto(reader.GetInt64(0), reader.GetString(1), reader.GetDecimal(2)));
            }
        }
        metrics.RecordQuery("products", timer.Elapsed.TotalSeconds);
        return products;
    }
}
