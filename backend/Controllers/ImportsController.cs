using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TrainingManagement.Api.Infrastructure;
using TrainingManagement.Api.Models;

namespace TrainingManagement.Api.Controllers;

[ApiController, Route("api/enterprise/imports"), Authorize(Roles = Roles.Administrators)]
public sealed class ImportsController(EmployeeImportService imports, IHttpClientFactory clients, IConfiguration config, IWebHostEnvironment environment) : ControllerBase
{
    [HttpPost("preview"), RequestSizeLimit(2_100_000)]
    public async Task<IActionResult> Preview(ImportRequest r, CancellationToken ct) => Ok(await imports.Preview(r.Csv, ct));
    [HttpPost("{id:guid}/commit")]
    public async Task<IActionResult> Commit(Guid id, CancellationToken ct) => Ok(await imports.Commit(id, ct));
    [HttpPost("hr-preview")]
    public async Task<IActionResult> HrPreview(CancellationToken ct)
    {
        if (!Uri.TryCreate(config["Hr:EmployeesEndpoint"], UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && !(environment.IsDevelopment() && uri.IsLoopback))) throw new ApiException(409, "請先設定 HTTPS 的 Hr:EmployeesEndpoint。");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (!string.IsNullOrWhiteSpace(config["Hr:ApiKey"])) request.Headers.Add("X-Api-Key", config["Hr:ApiKey"]);
        using var response = await clients.CreateClient("integrations").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw new ApiException(502, "人事系統無法回傳員工清單。");
        if (response.Content.Headers.ContentLength > 2_000_000) throw new ApiException(502, "人事系統資料超過 2 MB 限制。");
        await using var stream = await response.Content.ReadAsStreamAsync(ct); using var buffer = new MemoryStream();
        var block = new byte[8192]; int read;
        while ((read = await stream.ReadAsync(block, ct)) > 0) { if (buffer.Length + read > 2_000_000) throw new ApiException(502, "人事系統資料超過限制。"); await buffer.WriteAsync(block.AsMemory(0, read), ct); }
        return Ok(await imports.Preview(System.Text.Encoding.UTF8.GetString(buffer.ToArray()), ct));
    }
}
