using Grafana_Monitor.Models;

namespace Grafana_Monitor.Data;

public interface IOrderRepository
{
    Task<IReadOnlyList<OrderDto>> GetOrdersAsync(CancellationToken cancellationToken);
}
