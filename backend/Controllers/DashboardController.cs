using Microsoft.AspNetCore.Mvc;
using TrainingManagement.Api.Infrastructure;
namespace TrainingManagement.Api.Controllers;
[ApiController, Route("api/dashboard")]
public sealed class DashboardController(ReportingService reports) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(await reports.Dashboard(ct));
}
