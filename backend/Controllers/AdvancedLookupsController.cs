using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrainingManagement.Api.Data;
using TrainingManagement.Api.Infrastructure;
namespace TrainingManagement.Api.Controllers;
[ApiController,Route("api/advanced/lookups")]
public sealed class AdvancedLookupsController(TrainingDbContext db,DataScope scope,AdvancedAccess access):ControllerBase
{
    public sealed class Choice { public int Value {get;set;} public string Label {get;set;} = ""; }
    [HttpGet]
    public async Task<IActionResult> Lookup([FromQuery]string kind,[FromQuery]string? search,[FromQuery]int page=1,CancellationToken ct=default)
    {
        var text=search?.Trim()??"";IQueryable<Choice> query;
        switch(kind){
            case "courses":query=scope.Courses().Where(x=>text==""||x.Title.Contains(text)).Select(x=>new Choice{Value=x.Id,Label=x.Title});break;
            case "employees":query=(await access.People(ct)).Where(x=>text==""||x.Name.Contains(text)||x.EmployeeNumber.Contains(text)).Select(x=>new Choice{Value=x.Id,Label=x.Name+" · "+x.EmployeeNumber});break;
            case "users":var self=await db.AppUsers.Where(x=>x.Id==scope.UserId).Select(x=>x.EmployeeId).SingleAsync(ct);var department=await db.Employees.Where(x=>x.Id==self).Select(x=>x.Department).SingleOrDefaultAsync(ct);query=db.AppUsers.Where(x=>x.IsActive&&x.IsApproved&&(scope.IsGlobal||x.Id==scope.UserId||x.Employee!=null&&x.Employee.Department==department)&&(text==""||x.DisplayName.Contains(text)||x.Username.Contains(text))).Select(x=>new Choice{Value=x.Id,Label=x.DisplayName+" · "+x.Username});break;
            case "skills":query=db.Skills.Where(x=>text==""||x.Name.Contains(text)).Select(x=>new Choice{Value=x.Id,Label=x.Name});break;
            case "credentialTypes":query=db.CredentialTypes.Where(x=>text==""||x.Name.Contains(text)).Select(x=>new Choice{Value=x.Id,Label=x.Name});break;
            case "departments":var allowed=(await access.People(ct)).Select(x=>x.Department);query=db.Departments.Where(x=>(scope.IsGlobal||allowed.Contains(x.Name))&&(text==""||x.Name.Contains(text))).Select(x=>new Choice{Value=x.Id,Label=x.Name});break;
            case "series":query=db.CourseSeries.Where(x=>(scope.IsGlobal||scope.Courses().Any(c=>c.SeriesId==x.Id))&&(text==""||x.Title.Contains(text))).Select(x=>new Choice{Value=x.Id,Label=x.Title});break;
            case "policies":if(!scope.IsGlobal)throw new ApiException(403,"需要人資權限。");query=db.ApprovalPolicies.Where(x=>x.IsActive&&(text==""||x.Name.Contains(text))).Select(x=>new Choice{Value=x.Id,Label=x.Name});break;
            case "clients":if(!scope.IsGlobal)throw new ApiException(403,"需要人資權限。");query=db.IntegrationClients.Where(x=>x.IsActive&&x.ExpiresAt>DateTime.UtcNow&&(text==""||x.Name.Contains(text))).Select(x=>new Choice{Value=x.Id,Label=x.Name});break;
            case "budgets":var eid=await db.AppUsers.Where(x=>x.Id==scope.UserId).Select(x=>x.EmployeeId).SingleAsync(ct);var dept=await db.Employees.Where(x=>x.Id==eid).Select(x=>x.Department).SingleOrDefaultAsync(ct);query=db.Budgets.Where(x=>x.IsActive&&(scope.IsGlobal||x.OwnerUserId==scope.UserId||eid!=null&&(x.Department==null||x.Department==dept))&&(text==""||x.Name.Contains(text))).Select(x=>new Choice{Value=x.Id,Label=x.Name+" · "+x.Year+" "+x.Currency});break;
            case "activities":query=db.Activities.Where(x=>x.IsActive&&scope.Courses().Any(c=>c.Id==x.CourseId)&&(text==""||x.Title.Contains(text))).Select(x=>new Choice{Value=x.Id,Label=x.Title});break;
            default:throw new ApiException(400,"查詢類型不正確。");
        }
        var total=await query.CountAsync(ct);return Ok(new{total,items=await query.OrderBy(x=>x.Value).Skip((Math.Clamp(page,1,100000)-1)*30).Take(30).ToListAsync(ct)});
    }
}
