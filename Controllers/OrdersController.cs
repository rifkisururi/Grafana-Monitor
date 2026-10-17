using Grafana_Monitor.Services;
using Microsoft.AspNetCore.Mvc;

namespace Grafana_Monitor.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController(OrderService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var orders = await service.GetOrdersAsync(cancellationToken);
        return Ok(orders);
    }
}
