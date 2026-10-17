using Grafana_Monitor.Data;
using Grafana_Monitor.Models;

namespace Grafana_Monitor.Services;

public sealed class ProductService(IProductRepository repository)
{
    public Task<IReadOnlyList<ProductDto>> GetProductsAsync(CancellationToken cancellationToken) =>
        repository.GetProductsAsync(cancellationToken);
}
