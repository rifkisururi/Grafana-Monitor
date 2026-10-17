using Grafana_Monitor.Data;
using Grafana_Monitor.Models;

namespace Grafana_Monitor.Services;

public sealed class OrderService(IOrderRepository repository)
{
    public Task<IReadOnlyList<OrderDto>> GetOrdersAsync(CancellationToken cancellationToken) =>
        repository.GetOrdersAsync(cancellationToken);
}
