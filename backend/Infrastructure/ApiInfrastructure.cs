using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace TrainingManagement.Api.Infrastructure;

public sealed class ApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var invalidJson=exception is System.Text.Json.JsonException || exception is InvalidOperationException && exception.TargetSite?.DeclaringType?.Namespace?.StartsWith("System.Text.Json",StringComparison.Ordinal)==true;
        var statusCode = exception switch
        {
            ApiException api => api.StatusCode,
            DbUpdateException => StatusCodes.Status409Conflict,
            SqlException sql when sql.Number is 1205 or 51001 => StatusCodes.Status409Conflict,
            _ => invalidJson?StatusCodes.Status400BadRequest:StatusCodes.Status500InternalServerError
        };
        var message = exception switch
        {
            ApiException api => api.Message,
            DbUpdateException => "資料已變更或違反唯一性限制，請重新整理後再試。",
            SqlException sql when sql.Number is 1205 or 51001 => "資料正在更新，請重新整理後再試。",
            _ => invalidJson?"請求的 JSON 欄位型別不正確。":"系統暫時無法處理請求，請稍後再試。"
        };
        if (statusCode >= 500) logger.LogError(exception, "Request failed. Trace: {TraceId}", context.TraceIdentifier);
        if(context.Request.Path.StartsWithSegments("/scim/v2")) {
            context.Response.StatusCode=statusCode;context.Response.ContentType="application/scim+json";
            await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new { schemas=new[]{"urn:ietf:params:scim:api:messages:2.0:Error"},status=statusCode.ToString(),detail=message }),cancellationToken);
            return true;
        }
        await Results.Problem(statusCode: statusCode, title: message,
            extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier })
            .ExecuteAsync(context);
        return true;
    }
}

// Serialize related writes in this host. CourseWriteLock additionally serializes
// seat availability changes across API hosts within SQL Server transactions.
public sealed class MutationGate
{
    private readonly SemaphoreSlim semaphore = new(1, 1);

    public async ValueTask<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        return new Release(semaphore);
    }

    private sealed class Release(SemaphoreSlim semaphore) : IDisposable
    {
        private bool disposed;
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            semaphore.Release();
        }
    }
}
