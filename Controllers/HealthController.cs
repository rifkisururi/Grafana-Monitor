using Grafana_Monitor.Models;
using Microsoft.AspNetCore.Mvc;

namespace Grafana_Monitor.Controllers;

[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    [HttpGet("live")]
    public ActionResult<HealthDto> Live() => Ok(new HealthDto("ok"));
}
