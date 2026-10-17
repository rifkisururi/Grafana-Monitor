using Grafana_Monitor.Services;
using Microsoft.AspNetCore.Mvc;

namespace Grafana_Monitor.Controllers;

[ApiController]
[Route("api/products")]
public sealed class ProductsController(ProductService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var products = await service.GetProductsAsync(cancellationToken);
        return Ok(products);
    }
}
