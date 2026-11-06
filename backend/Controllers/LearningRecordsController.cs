using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrainingManagement.Api.Data;
using TrainingManagement.Api.Infrastructure;
using TrainingManagement.Api.Models;
namespace TrainingManagement.Api.Controllers;
[ApiController,Route("api/advanced")]
public sealed class LearningRecordsController(TrainingDbContext db,DataScope scope,AdvancedAccess access):ControllerBase
{
    [HttpGet("activities")]
    public async Task<IActionResult> Activities(CancellationToken ct)=>Ok(await db.Activities.Where(x=>x.IsActive&&scope.Courses().Any(c=>c.Id==x.CourseId)).OrderBy(x=>x.Title).Select(x=>new{x.Id,x.CourseId,courseTitle=x.Course.Title,x.Title,x.ActivityIri,x.LaunchUrl,x.Kind,x.ClientId,x.RequiredSeconds}).ToListAsync(ct));
    [HttpPost("activities"),Authorize(Roles=Roles.Administrators)]
    public async Task<IActionResult> Create(ActivityRequest r,CancellationToken ct)
    {
        if(!Uri.TryCreate(r.LaunchUrl,UriKind.Absolute,out var launch)||launch.Scheme!="https"||!string.IsNullOrEmpty(launch.UserInfo))throw new ApiException(400,"教材連結須使用 HTTPS，且不可包含帳密。");
        if(!await db.Courses.AnyAsync(x=>x.Id==r.CourseId,ct)||r.ClientId.HasValue&&!await db.IntegrationClients.AnyAsync(x=>x.Id==r.ClientId&&x.IsActive,ct))throw new ApiException(400,"課程或來源服務帳號不存在。");
        var a=new LearningActivity{CourseId=r.CourseId,Title=r.Title.Trim(),ActivityIri=r.ActivityIri,LaunchUrl=r.LaunchUrl,Kind=r.Kind,ClientId=r.ClientId,RequiredSeconds=r.RequiredSeconds};db.Activities.Add(a);await db.SaveChangesAsync(ct);return StatusCode(201,new{a.Id});
    }
    [HttpPost("activities/{id:int}/progress")]
    public async Task<IActionResult> Progress(int id,ActivityProgressRequest r,CancellationToken ct)
    {
        var eid=await access.Self(ct);var a=await db.Activities.SingleOrDefaultAsync(x=>x.Id==id&&x.IsActive,ct)??throw new ApiException(404,"教材不存在。");
        if(!await db.Enrollments.AnyAsync(x=>x.CourseId==a.CourseId&&x.EmployeeId==eid&&x.Status!=EnrollmentStatus.Cancelled,ct))throw new ApiException(403,"請先報名此課程。");
        var record=new LearningRecord{EmployeeId=eid,ActivityId=id,VerbIri="http://adlnet.gov/expapi/verbs/progressed",Source="SelfReported",Progress=r.Progress,DurationSeconds=r.DurationSeconds};db.LearningRecords.Add(record);await db.SaveChangesAsync(ct);return StatusCode(201,new{record.Id});
    }
    [HttpGet("learning-records")]
    public async Task<IActionResult> Records(CancellationToken ct){var people=await access.People(ct);return Ok(await db.LearningRecords.Where(x=>people.Any(p=>p.Id==x.EmployeeId)&&!x.IsVoided).OrderByDescending(x=>x.StoredAt).Take(500).Select(x=>new{x.Id,x.EmployeeId,employeeName=x.Employee.Name,x.ActivityId,activityTitle=x.Activity.Title,courseTitle=x.Activity.Course.Title,x.VerbIri,x.Source,x.Progress,x.Score,x.DurationSeconds,x.Timestamp,x.StoredAt}).ToListAsync(ct));}
}

[ApiController,Route("api/xapi"),Authorize(AuthenticationSchemes=IntegrationAuthentication.SchemeName)]
public sealed class ExperienceApiController(TrainingDbContext db,MutationGate gate):ControllerBase
{
    private int ClientId=>int.Parse(User.FindFirstValue("client_id")!);
    private void Check(bool write){if(!User.HasClaim("client_scope",write?"learning.write":"learning.read"))throw new ApiException(403,"服務帳號缺少學習紀錄範圍。");Response.Headers["X-Experience-API-Version"]="1.0.3";if(Request.Headers["X-Experience-API-Version"]!="1.0.3")throw new ApiException(400,"請提供 X-Experience-API-Version: 1.0.3。");}
    [HttpGet("about")]
    public IActionResult About(){Response.Headers["X-Experience-API-Version"]="1.0.3";return Ok(new{version=new[]{"1.0.3"},extensions=new Dictionary<string,object>{["https://training.example/profile"]="Statements gateway: registered Activity, Agent mbox, scoped client; document/state/profile APIs are not provided."}});}
    [HttpPost("statements"),RequestSizeLimit(2_000_000)]
    public async Task<IActionResult> Post([FromBody]JsonNode body,CancellationToken ct){Check(true);var statements=body is JsonArray a?a.OfType<JsonObject>().ToArray():body is JsonObject o?new[]{o}:[];if(statements.Length is 0 or >100||body is JsonArray array&&statements.Length!=array.Count)throw new ApiException(400,"每批須為 1 至 100 個 Statement。");using var write=await gate.EnterAsync(ct);await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);var ids=new List<Guid>();foreach(var s in statements){var id=s["id"] is null?Guid.NewGuid():ParseId(s["id"]!.GetValue<string>());if(ids.Contains(id))throw new ApiException(400,"同批 Statement ID 不可重複。");await Store(id,s,ct);ids.Add(id);}await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return Ok(ids);}
    [HttpPut("statements"),RequestSizeLimit(100000)]
    public async Task<IActionResult> Put([FromQuery]Guid statementId,[FromBody]JsonObject body,CancellationToken ct){Check(true);if(statementId==Guid.Empty||body["id"] is not null&&ParseId(body["id"]!.GetValue<string>())!=statementId)throw new ApiException(400,"Statement ID 不符。");using var write=await gate.EnterAsync(ct);await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);await Store(statementId,body,ct);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return NoContent();}
    private async Task Store(Guid id,JsonObject s,CancellationToken ct)
    {
        if(s.ToJsonString().Length>50000)throw new ApiException(400,"單筆 Statement 過大。");
        var hash=IntegrationAuthentication.Hash(Canonical(JsonSerializer.SerializeToElement(s)));var existing=await db.LearningRecords.FindAsync([id],ct);
        if(existing is not null){if(existing.ClientId!=ClientId||existing.StatementHash!=hash)throw new ApiException(409,"Statement ID 已存在且內容不同。");return;}
        if(s["actor"] is not JsonObject actor||s["verb"] is not JsonObject verb||s["object"] is not JsonObject activity)throw new ApiException(400,"Statement 必須包含 actor、verb 與 object。");
        var mailbox=actor["mbox"]?.GetValue<string>();if(mailbox is null||!mailbox.StartsWith("mailto:",StringComparison.OrdinalIgnoreCase)||actor["objectType"]?.GetValue<string>() is string actorType&&actorType!="Agent")throw new ApiException(400,"目前介面接受使用 mbox 的 Agent。");
        if(new[]{"mbox","mbox_sha1sum","openid","account"}.Count(k=>actor[k] is not null)!=1)throw new ApiException(400,"Agent 須使用單一識別方式。");
        var email=mailbox[7..].ToLowerInvariant();var employee=await db.Employees.SingleOrDefaultAsync(x=>x.Email==email&&x.IsActive,ct)??throw new ApiException(400,"Actor 未對應至在職員工。");
        var verbIri=verb["id"]?.GetValue<string>();if(!Uri.TryCreate(verbIri,UriKind.Absolute,out _))throw new ApiException(400,"verb.id 須為絕對 IRI。");
        LearningActivity target;
        if(verbIri=="http://adlnet.gov/expapi/verbs/voided"){
            if(activity["objectType"]?.GetValue<string>()!="StatementRef")throw new ApiException(400,"voided 事件須參照 StatementRef。");var refId=ParseId(activity["id"]?.GetValue<string>()??"");var previous=await db.LearningRecords.Include(x=>x.Activity).SingleOrDefaultAsync(x=>x.Id==refId&&x.ClientId==ClientId,ct)??throw new ApiException(404,"找不到可作廢的紀錄。");if(previous.VerbIri==verbIri)throw new ApiException(400,"不可作廢作廢事件。");previous.IsVoided=true;target=previous.Activity;
        }else{if(activity["objectType"] is not null && activity["objectType"]!.GetValue<string>()!="Activity")throw new ApiException(400,"此介面僅接受 Activity 物件。");var iri=activity["id"]?.GetValue<string>();target=await db.Activities.SingleOrDefaultAsync(x=>x.ActivityIri==iri&&x.IsActive&&x.ClientId==ClientId,ct)??throw new ApiException(403,"此服務未獲授權此學習活動。");}
        if(!await db.Enrollments.AnyAsync(x=>x.EmployeeId==employee.Id&&x.CourseId==target.CourseId&&x.Status!=EnrollmentStatus.Cancelled,ct))throw new ApiException(403,"Actor 未報名此活動對應課程。");
        decimal? score=null;var seconds=0;var progress=0m;
        if(s["result"] is JsonObject result){if(result["score"] is JsonObject scoreObject){if(scoreObject["scaled"] is not null){var scaled=scoreObject["scaled"]!.GetValue<decimal>();if(scaled is <(-1) or >1)throw new ApiException(400,"scaled 範圍為 -1 至 1。");score=scaled*100;}else if(scoreObject["raw"] is not null){score=scoreObject["raw"]!.GetValue<decimal>();if(score is <(-100000) or >100000)throw new ApiException(400,"分數超出範圍。");}}
            if(result["duration"] is not null){try{var d=System.Xml.XmlConvert.ToTimeSpan(result["duration"]!.GetValue<string>());if(d.TotalSeconds is <0 or >86400)throw new FormatException();seconds=(int)d.TotalSeconds;}catch(FormatException){throw new ApiException(400,"duration 須為一日內的 ISO 8601 時長。");}}
            if(result["completion"]?.GetValue<bool>()==true)progress=100;
            if(result["extensions"] is JsonObject extensions&&extensions["https://training.example/extensions/progress"] is JsonNode p){progress=p.GetValue<decimal>();if(progress is <0 or >100)throw new ApiException(400,"學習進度須為 0 至 100。");}}
        var timestamp=DateTimeOffset.UtcNow;if(s["timestamp"] is not null&&(!DateTimeOffset.TryParse(s["timestamp"]!.GetValue<string>(),out timestamp)||timestamp>DateTimeOffset.UtcNow.AddMinutes(5)))throw new ApiException(400,"timestamp 不正確。");
        var enriched=(JsonObject)s.DeepClone();enriched["id"]=id.ToString();enriched["stored"]=DateTimeOffset.UtcNow.ToString("O");enriched["timestamp"]=timestamp.ToString("O");enriched["version"]="1.0.0";enriched["authority"]=new JsonObject{["objectType"]="Agent",["account"]=new JsonObject{["homePage"]="https://training.example/integrations",["name"]=ClientId.ToString()}};
        db.LearningRecords.Add(new LearningRecord{Id=id,EmployeeId=employee.Id,ActivityId=target.Id,ClientId=ClientId,VerbIri=verbIri!,Source="xAPI",Score=score,DurationSeconds=seconds,Progress=progress,StatementJson=enriched.ToJsonString(),StatementHash=hash,Timestamp=timestamp.UtcDateTime});
    }
    [HttpGet("statements")]
    public async Task<IActionResult> Get([FromQuery]Guid? statementId,[FromQuery]Guid? voidedStatementId,[FromQuery]string? activity,[FromQuery]string? verb,[FromQuery]DateTime? since,[FromQuery]DateTime? until,[FromQuery]int limit=100,[FromQuery]int offset=0,CancellationToken ct=default)
    {
        Check(false);var q=db.LearningRecords.AsNoTracking().Where(x=>x.ClientId==ClientId);
        if(statementId.HasValue||voidedStatementId.HasValue){var id=statementId??voidedStatementId;var row=await q.SingleOrDefaultAsync(x=>x.Id==id&&x.IsVoided==voidedStatementId.HasValue,ct)??throw new ApiException(404,"找不到 Statement。");return Ok(JsonNode.Parse(row.StatementJson));}
        q=q.Where(x=>!x.IsVoided);if(activity is not null)q=q.Where(x=>x.Activity.ActivityIri==activity);if(verb is not null)q=q.Where(x=>x.VerbIri==verb);if(since.HasValue)q=q.Where(x=>x.StoredAt>since);if(until.HasValue)q=q.Where(x=>x.StoredAt<=until);limit=Math.Clamp(limit,1,500);offset=Math.Clamp(offset,0,1000000);
        var rows=await q.OrderBy(x=>x.StoredAt).ThenBy(x=>x.Id).Skip(offset).Take(limit+1).ToListAsync(ct);var more=rows.Count>limit?"/api/xapi/statements?offset="+(offset+limit)+"&limit="+limit+(activity is null?"":"&activity="+Uri.EscapeDataString(activity))+(verb is null?"":"&verb="+Uri.EscapeDataString(verb))+(since.HasValue?"&since="+Uri.EscapeDataString(since.Value.ToString("O")):"")+(until.HasValue?"&until="+Uri.EscapeDataString(until.Value.ToString("O")):""):"";
        return Ok(new{statements=rows.Take(limit).Select(x=>JsonNode.Parse(x.StatementJson)),more});
    }
    private static Guid ParseId(string s)=>Guid.TryParse(s,out var id)&&id!=Guid.Empty?id:throw new ApiException(400,"Statement ID 須為 UUID。");
    private static string Canonical(JsonElement e){using var stream=new MemoryStream();using(var w=new Utf8JsonWriter(stream)){Write(w,e);}return System.Text.Encoding.UTF8.GetString(stream.ToArray());}
    private static void Write(Utf8JsonWriter w,JsonElement e){switch(e.ValueKind){case JsonValueKind.Object:w.WriteStartObject();foreach(var p in e.EnumerateObject().OrderBy(x=>x.Name,StringComparer.Ordinal)){w.WritePropertyName(p.Name);Write(w,p.Value);}w.WriteEndObject();break;case JsonValueKind.Array:w.WriteStartArray();foreach(var a in e.EnumerateArray())Write(w,a);w.WriteEndArray();break;case JsonValueKind.Number:if(e.TryGetDecimal(out var n))w.WriteNumberValue(n);else w.WriteNumberValue(e.GetDouble());break;default:e.WriteTo(w);break;}}
}

[ApiController,Route("api/integrations"),Authorize(AuthenticationSchemes=IntegrationAuthentication.SchemeName,Policy="client:training.read")]
public sealed class PublicTrainingController(TrainingDbContext db):ControllerBase
{
    [HttpGet("courses")]
    public async Task<IActionResult> Courses([FromQuery]int page=1,CancellationToken ct=default){var q=db.Courses.Where(x=>x.Status==CourseStatus.Published);return Ok(new{total=await q.CountAsync(ct),items=await q.OrderBy(x=>x.Id).Skip((Math.Clamp(page,1,100000)-1)*100).Take(100).Select(x=>new{x.Id,x.Title,x.StartDate,x.EndDate,x.Capacity,x.SeriesId}).ToListAsync(ct)});}
}
