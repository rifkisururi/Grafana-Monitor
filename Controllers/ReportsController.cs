using Grafana_Monitor.Services;
using Microsoft.AspNetCore.Mvc;

namespace Grafana_Monitor.Controllers;

[ApiController]
[Route("api/reports")]
public sealed class ReportsController(ReportService service) : ControllerBase
{
    [HttpGet("heavy")]
    public async Task<IActionResult> GetHeavy(CancellationToken cancellationToken)
    {
        var report = await service.GetHeavyReportAsync(cancellationToken);
        return Ok(report);
    }
}
