using Grafana_Monitor.Models;
using Grafana_Monitor.Services;
using Microsoft.AspNetCore.Mvc;

namespace Grafana_Monitor.Controllers;

[ApiController]
[Route("api/reports")]
public sealed class ReportsController(ReportService service, ResourceStressService resourceStressService) : ControllerBase
{
    [HttpGet("normal")]
    public async Task<IActionResult> GetNormal(CancellationToken cancellationToken)
    {
        var report = await service.GetNormalReportAsync(cancellationToken);
        return Ok(report);
    }

    [HttpGet("heavy")]
    public async Task<IActionResult> GetHeavy(CancellationToken cancellationToken)
    {
        var report = await service.GetHeavyReportAsync(cancellationToken);
        return Ok(report);
    }

    [HttpGet("heavy-memory")]
    public Task<IActionResult> GetHeavyMemory(CancellationToken cancellationToken) =>
        RunResourceStressAsync(resourceStressService.RunMemoryAsync, cancellationToken);

    [HttpGet("heavy-cpu")]
    public Task<IActionResult> GetHeavyCpu(CancellationToken cancellationToken) =>
        RunResourceStressAsync(resourceStressService.RunCpuAsync, cancellationToken);

    [HttpGet("heavy-both")]
    public Task<IActionResult> GetHeavyBoth(CancellationToken cancellationToken) =>
        RunResourceStressAsync(resourceStressService.RunCombinedAsync, cancellationToken);

    private async Task<IActionResult> RunResourceStressAsync(
        Func<CancellationToken, Task<ResourceStressDto>> run,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await run(cancellationToken));
        }
        catch (ResourceStressLimitException)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "Resource stress limit reached; retry after current demo requests finish." });
        }
    }
}
