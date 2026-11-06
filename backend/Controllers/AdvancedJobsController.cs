using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TrainingManagement.Api.Infrastructure;
namespace TrainingManagement.Api.Controllers;
[ApiController,Route("api/advanced/jobs"),Authorize(Roles="Admin")]
public sealed class AdvancedJobsController(AdvancedJobs jobs):ControllerBase
{
    [HttpPost("run")]
    public async Task<IActionResult> Run(CancellationToken ct){await jobs.RunLocal(ct);return Ok(new{status="processed"});}
}
