using Microsoft.EntityFrameworkCore;
using TrainingManagement.Api.Data;
using TrainingManagement.Api.Models;
namespace TrainingManagement.Api.Infrastructure;
public sealed class HrSyncService(TrainingDbContext db,EmployeeImportService imports,IHttpClientFactory clients,IConfiguration config,IWebHostEnvironment environment)
{
    public async Task<string> Fetch(CancellationToken ct)
    {
        if(!Uri.TryCreate(config["Hr:EmployeesEndpoint"],UriKind.Absolute,out var uri)||(uri.Scheme!="https"&&!(environment.IsDevelopment()&&uri.IsLoopback)))throw new ApiException(409,"尚未設定有效的人事系統端點。");
        using var request=new HttpRequestMessage(HttpMethod.Get,uri);if(!string.IsNullOrWhiteSpace(config["Hr:ApiKey"]))request.Headers.Add("X-Api-Key",config["Hr:ApiKey"]);
        using var response=await clients.CreateClient("integrations").SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);if(!response.IsSuccessStatusCode)throw new ApiException(502,"人事來源無法取得。");
        await using var input=await response.Content.ReadAsStreamAsync(ct);using var buffer=new MemoryStream();var block=new byte[8192];int read;
        while((read=await input.ReadAsync(block,ct))>0){if(buffer.Length+read>2_000_000)throw new ApiException(502,"人事來源超過 2 MB。");await buffer.WriteAsync(block.AsMemory(0,read),ct);}
        try{return new System.Text.UTF8Encoding(false,true).GetString(buffer.ToArray());}catch(System.Text.DecoderFallbackException){throw new ApiException(502,"人事來源不是 UTF-8。");}
    }
    public async Task Run(int id,CancellationToken ct)
    {
        // Lease the schedule in SQL; import validation and network calls run outside that transaction.
        HrSyncSchedule schedule;
        await using(var tx=await db.Database.BeginTransactionAsync(ct)){await AdvancedAccess.Lock(db,$"hr-sync:{id}",ct);schedule=await db.HrSyncSchedules.FindAsync([id],ct)??throw new ApiException(404,"同步排程不存在。");if(schedule.LastStatus=="Running"&&schedule.LastRunAt>DateTime.UtcNow.AddMinutes(-10))throw new ApiException(409,"同步工作執行中。");schedule.LastStatus="Running";schedule.LastRunAt=DateTime.UtcNow;schedule.NextRunAt=DateTime.UtcNow.AddMinutes(schedule.IntervalMinutes);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);}
        var summary="";var status="Success";
        try{
            var csv=await Fetch(ct);var preview=await imports.Preview(csv,ct);
            if(!preview.Valid)throw new ApiException(400,"來源驗證失敗，未寫入任何員工資料。");
            if(!schedule.AllowDeactivation&&preview.Rows.Any(x=>!x.Employee.IsActive))throw new ApiException(409,"來源包含停用員工，但排程未允許自動停權。");
            await imports.Commit(preview.Id,ct);summary=$"已同步 {preview.Rows.Length} 筆員工；未出現在來源中的員工維持原狀。";
        }catch(Exception ex)when(ex is ApiException or HttpRequestException or TaskCanceledException or DbUpdateException){if(ct.IsCancellationRequested)throw;db.ChangeTracker.Clear();status="Failed";summary=ex is ApiException a?a.Message:"連線或寫入失敗；請檢查來源與同步紀錄。";}
        schedule=await db.HrSyncSchedules.FindAsync([id],ct)??throw new ApiException(404,"同步排程不存在。");schedule.LastStatus=status;schedule.LastSummary=summary;
        db.ProvisioningLogs.Add(new ProvisioningLog{Source="HR",Action="ScheduledSync",ResourceId=id.ToString(),Status=status,Summary=summary});await db.SaveChangesAsync(ct);
    }
}
