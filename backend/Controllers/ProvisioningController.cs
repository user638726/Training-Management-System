using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrainingManagement.Api.Data;
using TrainingManagement.Api.Infrastructure;
using TrainingManagement.Api.Models;
namespace TrainingManagement.Api.Controllers;
[ApiController,Route("scim/v2"),Produces("application/scim+json"),Authorize(AuthenticationSchemes=IntegrationAuthentication.SchemeName)]
public sealed class ProvisioningController(TrainingDbContext db,IPasswordHasher<AppUser> hasher,MutationGate gate):ControllerBase
{
    private const string UserSchema="urn:ietf:params:scim:schemas:core:2.0:User",GroupSchema="urn:ietf:params:scim:schemas:core:2.0:Group",ListSchema="urn:ietf:params:scim:api:messages:2.0:ListResponse";
    private int ClientId=>int.Parse(User.FindFirstValue("client_id")!);
    private void Need(string permission){if(!User.HasClaim("client_scope",permission))throw new ApiException(403,"服務帳號缺少此範圍。");}
    private static string? Text(JsonObject o,string name)=>o[name] is JsonValue v&&v.TryGetValue<string>(out var s)?s:null;
    private object UserResource(AppUser u,Employee? e)=>new Dictionary<string,object?>{
        ["schemas"]=new[]{UserSchema,"urn:ietf:params:scim:schemas:extension:enterprise:2.0:User"},["id"]=u.Id.ToString(),["externalId"]=u.ProvisioningExternalId,["userName"]=u.Username,["displayName"]=u.DisplayName,["active"]=u.IsActive,
        ["emails"]=e is null?Array.Empty<object>():new object[]{new{value=e.Email,type="work",primary=true}},
        ["urn:ietf:params:scim:schemas:extension:enterprise:2.0:User"]=new{department=e?.Department,employeeNumber=e?.EmployeeNumber},
        ["meta"]=new{resourceType="User",location=$"/scim/v2/Users/{u.Id}",version="W/\""+Convert.ToBase64String(u.RowVersion)+"\""}};
    private static object ListResult(int total,int start,IEnumerable<object> resources){var rows=resources.ToArray();return new Dictionary<string,object>{["schemas"]=new[]{ListSchema},["totalResults"]=total,["startIndex"]=start,["itemsPerPage"]=rows.Length,["Resources"]=rows};}
    [HttpGet("ServiceProviderConfig")]
    public IActionResult Configuration(){Need("scim.read");return Ok(new{schemas=new[]{"urn:ietf:params:scim:schemas:core:2.0:ServiceProviderConfig"},patch=new{supported=true},bulk=new{supported=false,maxOperations=0,maxPayloadSize=0},filter=new{supported=true,maxResults=500},changePassword=new{supported=false},sort=new{supported=false},etag=new{supported=false},authenticationSchemes=new[]{new{type="oauthbearertoken",name="Bearer token",description="Scoped integration credential"}}});}
    [HttpGet("ResourceTypes")]
    public IActionResult Resources(){Need("scim.read");return Ok(ListResult(2,1,new object[]{new{id="User",name="User",endpoint="/Users",schema=UserSchema},new{id="Group",name="Group",endpoint="/Groups",schema=GroupSchema}}));}
    [HttpGet("Schemas")]
    public IActionResult Schemas(){Need("scim.read");return Ok(ListResult(2,1,new object[]{new{id=UserSchema,name="User",attributes=new[]{new{name="userName",type="string",multiValued=false,required=true},new{name="displayName",type="string",multiValued=false,required=false},new{name="active",type="boolean",multiValued=false,required=false}}},new{id=GroupSchema,name="Group",attributes=new[]{new{name="displayName",type="string",multiValued=false,required=true}}}}));}
    [HttpGet("Users")]
    public async Task<IActionResult> Users([FromQuery]string? filter,[FromQuery]int startIndex=1,[FromQuery]int count=100,CancellationToken ct=default)
    {
        Need("scim.read");var q=db.AppUsers.AsNoTracking().Include(x=>x.Employee).Where(x=>x.ProvisioningClientId==ClientId);
        if(!string.IsNullOrWhiteSpace(filter)){var f=Filter(filter);q=f.Field switch{"userName"=>q.Where(x=>x.Username==f.Value),"externalId"=>q.Where(x=>x.ProvisioningExternalId==f.Value),_=>throw new ApiException(400,"不支援此 SCIM 篩選欄位。")};}
        var size=Math.Clamp(count,0,500);var start=Math.Clamp(startIndex,1,1000000);var rows=await q.OrderBy(x=>x.Id).Skip(start-1).Take(size).ToListAsync(ct);
        return Ok(ListResult(await q.CountAsync(ct),start,rows.Select(x=>UserResource(x,x.Employee))));
    }
    [HttpGet("Users/{id:int}")]
    public async Task<IActionResult> GetUser(int id,CancellationToken ct){Need("scim.read");var u=await Managed(id,ct);return Ok(UserResource(u,u.Employee));}
    [HttpPost("Users"),RequestSizeLimit(100000)]
    public async Task<IActionResult> Create([FromBody]JsonObject r,CancellationToken ct)
    {
        Need("scim.write");using var write=await gate.EnterAsync(ct);await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);
        var username=Text(r,"userName")?.Trim();if(string.IsNullOrWhiteSpace(username)||username.Length>80||username.Any(char.IsWhiteSpace))throw new ApiException(400,"userName 必填、不可包含空白且最多 80 字元。");
        if(r["password"] is not null||r["roles"] is JsonArray{Count:>0})throw new ApiException(400,"SCIM 不接受密碼或角色指派。");
        var external=Text(r,"externalId")?.Trim()??username;if(external.Length>200)throw new ApiException(400,"externalId 過長。");
        var email=Email(r,username);var name=Display(r,username);var extension=r["urn:ietf:params:scim:schemas:extension:enterprise:2.0:User"] as JsonObject;
        var number=Text(extension??new(),"employeeNumber")?.Trim().ToUpperInvariant()??$"SCIM-{ClientId}-{Guid.NewGuid():N}"[..Math.Min(29,$"SCIM-{ClientId}-{Guid.NewGuid():N}".Length)];
        if(number.Length>30||!Regex.IsMatch(number,"^[A-Za-z0-9_-]+$"))throw new ApiException(400,"employeeNumber 格式不正確。");
        if(await db.AppUsers.AnyAsync(x=>x.NormalizedUsername==username.ToUpper()||x.ProvisioningClientId==ClientId&&x.ProvisioningExternalId==external,ct)||await db.Employees.AnyAsync(x=>x.Email==email||x.EmployeeNumber==number,ct))throw new ApiException(409,"帳號、外部識別碼、郵件或員工編號已存在；既有身分須由管理員明確綁定。");
        var department=Text(extension??new(),"department")?.Trim()??"未分配";var title=Text(r,"title")?.Trim()??"員工";
        if(department.Length is 0 or >80||title.Length is 0 or >100)throw new ApiException(400,"部門或職稱長度不正確。");
        if(!await db.Departments.AnyAsync(x=>x.Name==department,ct))db.Departments.Add(new Department{Name=department});
        var active=r["active"]?.GetValue<bool>()??true;
        var employee=new Employee{Name=name,EmployeeNumber=number,Email=email,Department=department,JobTitle=title,IsActive=active};
        var user=new AppUser{Username=username,NormalizedUsername=username.ToUpperInvariant(),DisplayName=name,Employee=employee,Role="Employee",IsActive=active,IsApproved=true,ProvisioningClientId=ClientId,ProvisioningExternalId=external};user.PasswordHash=hasher.HashPassword(user,Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48)));
        db.AppUsers.Add(user);await db.SaveChangesAsync(ct);Log("CreateUser",user.Id.ToString(),"建立一般員工帳號");await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);Response.Headers.Location=$"/scim/v2/Users/{user.Id}";return StatusCode(201,UserResource(user,employee));
    }
    [HttpPut("Users/{id:int}"),RequestSizeLimit(100000)]
    public Task<IActionResult> Replace(int id,[FromBody]JsonObject r,CancellationToken ct)=>UpdateUser(id,r,false,ct);
    [HttpPatch("Users/{id:int}"),RequestSizeLimit(100000)]
    public Task<IActionResult> Patch(int id,[FromBody]JsonObject r,CancellationToken ct)=>UpdateUser(id,r,true,ct);
    private async Task<IActionResult> UpdateUser(int id,JsonObject r,bool patch,CancellationToken ct)
    {
        Need("scim.write");using var write=await gate.EnterAsync(ct);await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);var u=await Managed(id,ct);if(u.Role!="Employee")throw new ApiException(403,"高權限帳號須由管理員管理，不接受 SCIM 修改。");
        var values=patch?PatchValues(r):r;
        foreach(var pair in values){var name=pair.Key;var value=pair.Value;
            switch(name){
                case "schemas":case "id":case "meta":break;
                case "active":u.IsActive=value?.GetValue<bool>()??false;u.Employee!.IsActive=u.IsActive;break;
                case "displayName":case "name.formatted":var display=value?.GetValue<string>()?.Trim();if(string.IsNullOrWhiteSpace(display)||display.Length>100)throw new ApiException(400,"顯示名稱長度不正確。");u.DisplayName=display;u.Employee!.Name=display;break;
                case "name":if(value is JsonObject n&&Text(n,"formatted") is string formatted){if(formatted.Length>100)throw new ApiException(400,"姓名過長。");u.DisplayName=formatted;u.Employee!.Name=formatted;}break;
                case "userName":var username=value?.GetValue<string>()?.Trim();if(string.IsNullOrWhiteSpace(username)||username.Length>80||username.Any(char.IsWhiteSpace))throw new ApiException(400,"userName 格式不正確。");u.Username=username;u.NormalizedUsername=username.ToUpperInvariant();break;
                case "externalId":var external=value?.GetValue<string>();if(string.IsNullOrWhiteSpace(external)||external.Length>200)throw new ApiException(400,"externalId 不正確。");u.ProvisioningExternalId=external;break;
                case "emails":u.Employee!.Email=Email(new JsonObject{["emails"]=value?.DeepClone()},u.Username);break;
                case "title":var title=value?.GetValue<string>()?.Trim();if(string.IsNullOrWhiteSpace(title)||title.Length>100)throw new ApiException(400,"職稱不正確。");u.Employee!.JobTitle=title;break;
                case "urn:ietf:params:scim:schemas:extension:enterprise:2.0:User":if(value is JsonObject e&&Text(e,"department") is string department){department=department.Trim();if(department.Length is 0 or >80)throw new ApiException(400,"部門不正確。");u.Employee!.Department=department;if(!await db.Departments.AnyAsync(x=>x.Name==department,ct))db.Departments.Add(new Department{Name=department});}break;
                default:throw new ApiException(400,$"不支援的 SCIM 更新欄位：{name}");
            }
        }
        u.TokenVersion++;Log("UpdateUser",id.ToString(),"更新身分、群組資料或啟用狀態");await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return Ok(UserResource(u,u.Employee));
    }
    [HttpDelete("Users/{id:int}")]
    public async Task<IActionResult> Delete(int id,CancellationToken ct){Need("scim.write");var u=await Managed(id,ct);if(u.Role!="Employee")throw new ApiException(403,"不可透過 SCIM 停用高權限帳號。");u.IsActive=false;u.Employee!.IsActive=false;u.TokenVersion++;Log("DeactivateUser",id.ToString(),"保留歷史並停用帳號");await db.SaveChangesAsync(ct);return NoContent();}
    private async Task<AppUser> Managed(int id,CancellationToken ct)=>await db.AppUsers.Include(x=>x.Employee).SingleOrDefaultAsync(x=>x.Id==id&&x.ProvisioningClientId==ClientId,ct)??throw new ApiException(404,"找不到此來源管理的使用者。");
    private static string Email(JsonObject r,string fallback){var value=(r["emails"] as JsonArray)?.OfType<JsonObject>().Select(x=>Text(x,"value")).FirstOrDefault(x=>!string.IsNullOrWhiteSpace(x))??fallback;try{var address=new System.Net.Mail.MailAddress(value);if(address.Address!=value||value.Length>200)throw new FormatException();return value.Trim().ToLowerInvariant();}catch(FormatException){throw new ApiException(400,"必須提供有效的工作電子郵件。");}}
    private static string Display(JsonObject r,string fallback){var name=Text(r,"displayName")??(r["name"] is JsonObject n?Text(n,"formatted"):null)??fallback;if(string.IsNullOrWhiteSpace(name)||name.Length>100)throw new ApiException(400,"姓名長度不正確。");return name.Trim();}
    private static (string Field,string Value) Filter(string text){var m=Regex.Match(text,"^(userName|externalId|displayName) eq \"([^\"]{1,200})\"$",RegexOptions.CultureInvariant);if(!m.Success)throw new ApiException(400,"支援 userName、externalId 或 displayName 的 eq 篩選。");return(m.Groups[1].Value,m.Groups[2].Value);}
    private static JsonObject PatchValues(JsonObject r){if(r["Operations"] is not JsonArray ops||ops.Count is 0 or >100)throw new ApiException(400,"需要有效的 Operations。");var result=new JsonObject();foreach(var op in ops.OfType<JsonObject>()){var action=Text(op,"op")?.ToLowerInvariant();if(action is not("add" or "replace" or "remove"))throw new ApiException(400,"不支援的 PATCH 動作。");var path=Text(op,"path");if(path is null&&op["value"] is JsonObject v){foreach(var p in v)result[p.Key]=p.Value?.DeepClone();}else if(path is not null)result[path]=action=="remove"?null:op["value"]?.DeepClone();else throw new ApiException(400,"PATCH 路徑不正確。");}return result;}
    private void Log(string action,string id,string summary)=>db.ProvisioningLogs.Add(new ProvisioningLog{ClientId=ClientId,Action=action,ResourceId=id,Summary=summary});
    private object GroupResource(ProvisioningGroup g)=>new{schemas=new[]{GroupSchema},id=g.Id.ToString(),externalId=g.ExternalId,displayName=g.DisplayName,members=(JsonSerializer.Deserialize<int[]>(g.MembersJson)??[]).Select(x=>new{value=x.ToString()}),meta=new{resourceType="Group",lastModified=g.ModifiedAt,location=$"/scim/v2/Groups/{g.Id}"}};
    [HttpGet("Groups")]
    public async Task<IActionResult> Groups([FromQuery]string? filter,[FromQuery]int startIndex=1,[FromQuery]int count=100,CancellationToken ct=default){Need("scim.read");var q=db.ProvisioningGroups.Where(x=>x.ClientId==ClientId);if(!string.IsNullOrWhiteSpace(filter)){var f=Filter(filter);q=f.Field=="externalId"?q.Where(x=>x.ExternalId==f.Value):f.Field=="displayName"?q.Where(x=>x.DisplayName==f.Value):throw new ApiException(400,"群組篩選欄位不正確。");}var start=Math.Clamp(startIndex,1,1000000);var rows=await q.OrderBy(x=>x.Id).Skip(start-1).Take(Math.Clamp(count,0,500)).ToListAsync(ct);return Ok(ListResult(await q.CountAsync(ct),start,rows.Select(GroupResource)));}
    [HttpGet("Groups/{id:int}")]
    public async Task<IActionResult> Group(int id,CancellationToken ct){Need("scim.read");var g=await db.ProvisioningGroups.SingleOrDefaultAsync(x=>x.Id==id&&x.ClientId==ClientId,ct)??throw new ApiException(404,"找不到群組。");return Ok(GroupResource(g));}
    [HttpPost("Groups")]
    public Task<IActionResult> CreateGroup([FromBody]JsonObject r,CancellationToken ct)=>SaveGroup(null,r,false,ct);
    [HttpPut("Groups/{id:int}")]
    public Task<IActionResult> PutGroup(int id,[FromBody]JsonObject r,CancellationToken ct)=>SaveGroup(id,r,false,ct);
    [HttpPatch("Groups/{id:int}")]
    public Task<IActionResult> PatchGroup(int id,[FromBody]JsonObject r,CancellationToken ct)=>SaveGroup(id,r,true,ct);
    private async Task<IActionResult> SaveGroup(int? id,JsonObject r,bool patch,CancellationToken ct)
    {
        Need("scim.write");using var write=await gate.EnterAsync(ct);await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);
        var g=id.HasValue?await db.ProvisioningGroups.SingleOrDefaultAsync(x=>x.Id==id&&x.ClientId==ClientId,ct)??throw new ApiException(404,"找不到群組。"):new ProvisioningGroup{ClientId=ClientId};
        var previous=JsonSerializer.Deserialize<int[]>(g.MembersJson)??[];var members=previous.ToHashSet();
        void SetMembers(JsonNode? node,bool replace,bool remove){if(node is not JsonArray a)throw new ApiException(400,"members 須為陣列。");if(replace)members.Clear();foreach(var m in a.OfType<JsonObject>()){if(!int.TryParse(Text(m,"value"),out var uid))throw new ApiException(400,"members.value 不正確。");if(remove)members.Remove(uid);else members.Add(uid);}}
        if(patch){if(r["Operations"] is not JsonArray ops||ops.Count>100)throw new ApiException(400,"Operations 不正確。");foreach(var o in ops.OfType<JsonObject>()){var action=Text(o,"op")?.ToLowerInvariant();var path=Text(o,"path");if(action is not("add" or "replace" or "remove"))throw new ApiException(400,"PATCH 動作不正確。");if(path is null&&o["value"] is JsonObject v){if(Text(v,"displayName") is string n)g.DisplayName=n;if(v["members"] is JsonArray a)SetMembers(a,action=="replace",action=="remove");}else if(path=="members")SetMembers(o["value"],action=="replace",action=="remove");else if(path=="displayName")g.DisplayName=o["value"]?.GetValue<string>()??"";else if(action=="remove"&&path is not null){var match=Regex.Match(path,"^members\\[value eq \"([0-9]+)\"\\]$");if(!match.Success)throw new ApiException(400,"群組 PATCH 路徑不正確。");members.Remove(int.Parse(match.Groups[1].Value));}else throw new ApiException(400,"群組 PATCH 路徑不正確。");}}
        else{g.DisplayName=Text(r,"displayName")?.Trim()??"";g.ExternalId=Text(r,"externalId")??g.DisplayName;SetMembers(r["members"]??new JsonArray(),true,false);}
        if(string.IsNullOrWhiteSpace(g.DisplayName)||g.DisplayName.Length>200||g.ExternalId.Length>200||members.Count>1000)throw new ApiException(400,"群組名稱或成員數不正確。");
        if(await db.AppUsers.CountAsync(x=>members.Contains(x.Id)&&x.ProvisioningClientId==ClientId&&x.Role=="Employee",ct)!=members.Count)throw new ApiException(403,"群組只能包含此來源管理的一般使用者。");
        g.MembersJson=JsonSerializer.Serialize(members.Order());g.ModifiedAt=DateTime.UtcNow;if(!id.HasValue)db.ProvisioningGroups.Add(g);
        if(g.Department is not null){foreach(var u in await db.AppUsers.Include(x=>x.Employee).Where(x=>members.Contains(x.Id)&&x.ProvisioningClientId==ClientId).ToListAsync(ct)){u.Employee!.Department=g.Department;u.TokenVersion++;}foreach(var u in await db.AppUsers.Include(x=>x.Employee).Where(x=>previous.Contains(x.Id)&&!members.Contains(x.Id)&&x.ProvisioningClientId==ClientId).ToListAsync(ct)){if(u.Employee!.Department==g.Department){u.Employee.Department="未分配";u.TokenVersion++;}}}
        await db.SaveChangesAsync(ct);Log("UpdateGroup",g.Id.ToString(),$"同步 {members.Count} 名成員");await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return StatusCode(id.HasValue?200:201,GroupResource(g));
    }
    [HttpDelete("Groups/{id:int}")]
    public async Task<IActionResult> DeleteGroup(int id,CancellationToken ct){Need("scim.write");var g=await db.ProvisioningGroups.SingleOrDefaultAsync(x=>x.Id==id&&x.ClientId==ClientId,ct)??throw new ApiException(404,"找不到群組。");if(g.Department is not null)throw new ApiException(409,"請先由管理員解除部門對應再刪除群組。");db.ProvisioningGroups.Remove(g);Log("DeleteGroup",id.ToString(),"移除群組，不刪除員工");await db.SaveChangesAsync(ct);return NoContent();}
}
