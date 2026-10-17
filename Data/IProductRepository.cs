using Grafana_Monitor.Models;

namespace Grafana_Monitor.Data;

public interface IProductRepository
{
    Task<IReadOnlyList<ProductDto>> GetProductsAsync(CancellationToken cancellationToken);
}
