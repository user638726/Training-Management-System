using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrainingManagement.Api.Data;
using TrainingManagement.Api.Infrastructure;
using TrainingManagement.Api.Models;
namespace TrainingManagement.Api.Controllers;
[ApiController,Route("api/advanced")]
public sealed class TalentController(TrainingDbContext db,DataScope scope,AdvancedAccess access,MutationGate gate,TrainingService training):ControllerBase
{
    [HttpGet("skills")]
    public async Task<IActionResult> Skills(CancellationToken ct)=>Ok(await db.Skills.OrderBy(x=>x.Category).ThenBy(x=>x.Name).ToListAsync(ct));
    [HttpPost("skills"),Authorize(Roles=Roles.Administrators)]
    public async Task<IActionResult> Skill(SkillRequest r,CancellationToken ct){var s=new Skill{Name=r.Name.Trim(),Category=r.Category.Trim(),Description=r.Description.Trim()};db.Skills.Add(s);await db.SaveChangesAsync(ct);return StatusCode(201,new{s.Id});}
    [HttpGet("skill-requirements")]
    public async Task<IActionResult> Requirements(CancellationToken ct)=>Ok(await db.JobSkillRequirements.OrderBy(x=>x.JobTitle).Select(x=>new{x.Id,x.JobTitle,x.SkillId,skillName=x.Skill.Name,x.TargetLevel}).ToListAsync(ct));
    [HttpPost("skill-requirements"),Authorize(Roles=Roles.Administrators)]
    public async Task<IActionResult> Requirement(SkillRequirementRequest r,CancellationToken ct){if(!await db.Skills.AnyAsync(x=>x.Id==r.SkillId,ct))throw new ApiException(400,"職能不存在。");var e=await db.JobSkillRequirements.SingleOrDefaultAsync(x=>x.JobTitle==r.JobTitle.Trim()&&x.SkillId==r.SkillId,ct);if(e is null){e=new JobSkillRequirement{JobTitle=r.JobTitle.Trim(),SkillId=r.SkillId};db.JobSkillRequirements.Add(e);}e.TargetLevel=r.TargetLevel;await db.SaveChangesAsync(ct);return Ok(new{e.Id});}
    [HttpGet("skill-courses")]
    public async Task<IActionResult> CourseMappings(CancellationToken ct)=>Ok(await db.SkillCourses.Where(x=>scope.Courses().Any(c=>c.Id==x.CourseId)).Select(x=>new{x.SkillId,x.CourseId,x.Level}).ToListAsync(ct));
    [HttpPost("skill-courses"),Authorize(Roles=Roles.Administrators)]
    public async Task<IActionResult> MapCourse(SkillCourseRequest r,CancellationToken ct){if(!await db.Skills.AnyAsync(x=>x.Id==r.SkillId,ct)||!await db.Courses.AnyAsync(x=>x.Id==r.CourseId,ct))throw new ApiException(400,"職能或課程不存在。");var map=await db.SkillCourses.FindAsync([r.SkillId,r.CourseId],ct);if(map is null){map=new SkillCourse{SkillId=r.SkillId,CourseId=r.CourseId};db.SkillCourses.Add(map);}map.Level=r.Level;await db.SaveChangesAsync(ct);return Ok();}
    [HttpGet("skill-matrix")]
    public async Task<IActionResult> Matrix([FromQuery]int page=1,[FromQuery]string? search=null,CancellationToken ct=default)
    {
        var people=await access.People(ct);if(!string.IsNullOrWhiteSpace(search))people=people.Where(x=>x.Name.Contains(search)||x.EmployeeNumber.Contains(search)||x.Department.Contains(search));
        var total=await people.CountAsync(ct);var employees=await people.OrderBy(x=>x.Id).Skip((Math.Clamp(page,1,100000)-1)*30).Take(30).ToListAsync(ct);var ids=employees.Select(x=>x.Id).ToArray();
        var requirements=await db.JobSkillRequirements.Include(x=>x.Skill).ToListAsync(ct);var assessments=await db.SkillAssessments.Where(x=>ids.Contains(x.EmployeeId)&&x.Status=="Approved").OrderByDescending(x=>x.ReviewedAt).ThenByDescending(x=>x.Id).ToListAsync(ct);
        var maps=await db.SkillCourses.Where(x=>scope.Courses().Any(c=>c.Id==x.CourseId&&c.Status==CourseStatus.Published)).ToListAsync(ct);
        return Ok(new{total,items=employees.Select(e=>new{e.Id,e.Name,e.Department,e.JobTitle,skills=requirements.Where(r=>r.JobTitle==e.JobTitle).Select(r=>{var current=assessments.FirstOrDefault(a=>a.EmployeeId==e.Id&&a.SkillId==r.SkillId)?.Level??0;return new{r.SkillId,skillName=r.Skill.Name,r.TargetLevel,currentLevel=current,gap=Math.Max(0,r.TargetLevel-current),recommendedCourseIds=maps.Where(m=>m.SkillId==r.SkillId&&m.Level>current).OrderBy(m=>m.Level).Select(m=>m.CourseId)};})})});
    }
    [HttpGet("assessments")]
    public async Task<IActionResult> Assessments(CancellationToken ct){var people=await access.People(ct);return Ok(await db.SkillAssessments.Where(x=>people.Any(p=>p.Id==x.EmployeeId)).OrderByDescending(x=>x.Id).Take(500).Select(x=>new{x.Id,x.EmployeeId,employeeName=x.Employee.Name,x.SkillId,skillName=x.Skill.Name,x.Level,x.Evidence,x.Status,x.Comment,x.CreatedAt,x.ReviewedAt,rowVersion=Convert.ToBase64String(x.RowVersion)}).ToListAsync(ct));}
    [HttpPost("assessments")]
    public async Task<IActionResult> Assess(AssessmentRequest r,CancellationToken ct){var eid=await access.Self(ct);if(!await db.Skills.AnyAsync(x=>x.Id==r.SkillId,ct))throw new ApiException(400,"職能不存在。");var a=new SkillAssessment{EmployeeId=eid,SkillId=r.SkillId,Level=r.Level,Evidence=r.Evidence.Trim()};db.SkillAssessments.Add(a);await db.SaveChangesAsync(ct);return StatusCode(201,new{a.Id});}
    [HttpPost("assessments/{id:int}/decision"),Authorize(Roles=Roles.Reviewers)]
    public async Task<IActionResult> Review(int id,ApprovalDecision r,CancellationToken ct){var a=await db.SkillAssessments.FindAsync([id],ct)??throw new ApiException(404,"找不到評量。");await access.ReviewEmployee(a.EmployeeId,ct);DataScope.CheckVersion(a.RowVersion,r.RowVersion);if(a.Status!="Pending")throw new ApiException(409,"評量已審核。");a.Status=r.Approve?"Approved":"Rejected";a.ReviewerId=scope.UserId;a.Comment=r.Comment.Trim();a.ReviewedAt=DateTime.UtcNow;await db.SaveChangesAsync(ct);return NoContent();}
    [HttpGet("goals")]
    public async Task<IActionResult> Goals(CancellationToken ct){var people=await access.People(ct);return Ok(await db.Goals.Where(x=>people.Any(p=>p.Id==x.EmployeeId)).OrderBy(x=>x.DueAt).Take(500).Select(x=>new{x.Id,x.EmployeeId,employeeName=x.Employee.Name,x.Title,x.SkillId,skillName=x.Skill.Name,x.TargetLevel,x.DueAt,x.Status,x.CourseIdsJson,rowVersion=Convert.ToBase64String(x.RowVersion)}).ToListAsync(ct));}
    [HttpPost("goals")]
    public async Task<IActionResult> CreateGoal(GoalRequest r,CancellationToken ct){var eid=r.EmployeeId??await access.Self(ct);if(!await db.AppUsers.AnyAsync(x=>x.Id==scope.UserId&&x.EmployeeId==eid,ct))await access.ReviewEmployee(eid,ct);await ValidateGoal(r,eid,ct);var g=new DevelopmentGoal{EmployeeId=eid,Title=r.Title.Trim(),SkillId=r.SkillId,TargetLevel=r.TargetLevel,DueAt=r.DueAt.UtcDateTime,Status=r.Status,CourseIdsJson=JsonSerializer.Serialize(r.CourseIds.Distinct())};db.Goals.Add(g);await db.SaveChangesAsync(ct);return StatusCode(201,new{g.Id});}
    [HttpPut("goals/{id:int}")]
    public async Task<IActionResult> UpdateGoal(int id,GoalRequest r,CancellationToken ct){var g=await db.Goals.FindAsync([id],ct)??throw new ApiException(404,"找不到成長計畫。");var self=await db.AppUsers.AnyAsync(x=>x.Id==scope.UserId&&x.EmployeeId==g.EmployeeId,ct);if(!self)await access.ReviewEmployee(g.EmployeeId,ct);if(string.IsNullOrWhiteSpace(r.RowVersion))throw new ApiException(400,"請提供版本。");DataScope.CheckVersion(g.RowVersion,r.RowVersion);await ValidateGoal(r,g.EmployeeId,ct);g.Title=r.Title.Trim();g.SkillId=r.SkillId;g.TargetLevel=r.TargetLevel;g.DueAt=r.DueAt.UtcDateTime;g.Status=r.Status;g.CourseIdsJson=JsonSerializer.Serialize(r.CourseIds.Distinct());await db.SaveChangesAsync(ct);return NoContent();}
    private async Task ValidateGoal(GoalRequest r,int employeeId,CancellationToken ct){if(r.DueAt==default||!await db.Skills.AnyAsync(x=>x.Id==r.SkillId,ct)||await scope.Courses().CountAsync(x=>r.CourseIds.Contains(x.Id),ct)!=r.CourseIds.Distinct().Count())throw new ApiException(400,"職能、課程或期限不正確。");if(r.Status=="Completed"){var level=await db.SkillAssessments.Where(x=>x.EmployeeId==employeeId&&x.SkillId==r.SkillId&&x.Status=="Approved").OrderByDescending(x=>x.ReviewedAt).ThenByDescending(x=>x.Id).Select(x=>(int?)x.Level).FirstOrDefaultAsync(ct);if(level<r.TargetLevel||level is null)throw new ApiException(409,"完成目標前需有達到目標程度的已核准評量。");}}
    [HttpGet("credential-types")]
    public async Task<IActionResult> Types(CancellationToken ct)=>Ok(await db.CredentialTypes.OrderBy(x=>x.Name).ToListAsync(ct));
    [HttpPost("credential-types"),Authorize(Roles=Roles.Administrators)]
    public async Task<IActionResult> Type(SkillRequest r,CancellationToken ct){var t=new CredentialType{Name=r.Name.Trim(),Description=r.Description.Trim()};db.CredentialTypes.Add(t);await db.SaveChangesAsync(ct);return StatusCode(201,new{t.Id});}
    [HttpGet("credential-requirements")]
    public async Task<IActionResult> CredentialRequirements(CancellationToken ct)=>Ok(await db.CredentialRequirements.Select(x=>new{x.Id,x.JobTitle,x.CredentialTypeId,name=x.CredentialType.Name,x.RenewalNoticeDays}).ToListAsync(ct));
    [HttpPost("credential-requirements"),Authorize(Roles=Roles.Administrators)]
    public async Task<IActionResult> CredentialRequirement(CredentialRequirementRequest r,CancellationToken ct){if(!await db.CredentialTypes.AnyAsync(x=>x.Id==r.CredentialTypeId,ct))throw new ApiException(400,"證照類型不存在。");var e=await db.CredentialRequirements.SingleOrDefaultAsync(x=>x.JobTitle==r.JobTitle.Trim()&&x.CredentialTypeId==r.CredentialTypeId,ct);if(e is null){e=new CredentialRequirement{JobTitle=r.JobTitle.Trim(),CredentialTypeId=r.CredentialTypeId};db.CredentialRequirements.Add(e);}e.RenewalNoticeDays=r.RenewalNoticeDays;await db.SaveChangesAsync(ct);return Ok(new{e.Id});}
    [HttpGet("credentials")]
    public async Task<IActionResult> Credentials(CancellationToken ct){var people=await access.People(ct);return Ok(await db.ExternalCredentials.Where(x=>people.Any(p=>p.Id==x.EmployeeId)).OrderByDescending(x=>x.Id).Take(500).Select(x=>new{x.Id,x.EmployeeId,employeeName=x.Employee.Name,x.CredentialTypeId,name=x.CredentialType.Name,x.Issuer,x.CredentialNumber,x.IssuedAt,x.ExpiresAt,x.ReplacesId,x.Status,x.FileName,x.Comment,x.ReviewedAt,rowVersion=Convert.ToBase64String(x.RowVersion)}).ToListAsync(ct));}
    [HttpPost("credentials"),RequestSizeLimit(11_000_000)]
    public async Task<IActionResult> Upload([FromForm]CredentialUpload r,CancellationToken ct)
    {
        var eid=await access.Self(ct);var file=await AdvancedAccess.Evidence(r.File,ct);if(r.IssuedAt==default||r.IssuedAt>DateTimeOffset.UtcNow.AddDays(1)||r.ExpiresAt<=r.IssuedAt)throw new ApiException(400,"證照日期不正確。");
        using var write=await gate.EnterAsync(ct);await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);
        if(!await db.CredentialTypes.AnyAsync(x=>x.Id==r.CredentialTypeId,ct))throw new ApiException(400,"證照類型不存在。");var number=r.CredentialNumber.Trim().ToUpperInvariant();
        if(await db.ExternalCredentials.AnyAsync(x=>x.CredentialTypeId==r.CredentialTypeId&&x.CredentialNumber==number&&x.EmployeeId!=eid,ct))throw new ApiException(409,"證照編號已由其他員工提出。");
        if(r.ReplacesId.HasValue&&!await db.ExternalCredentials.AnyAsync(x=>x.Id==r.ReplacesId&&x.EmployeeId==eid&&x.CredentialTypeId==r.CredentialTypeId&&x.Status=="Approved",ct))throw new ApiException(400,"續證來源須為本人同類型的已核准證照。");
        var e=new ExternalCredential{EmployeeId=eid,CredentialTypeId=r.CredentialTypeId,Issuer=r.Issuer.Trim(),CredentialNumber=number,IssuedAt=r.IssuedAt.UtcDateTime,ExpiresAt=r.ExpiresAt?.UtcDateTime,ReplacesId=r.ReplacesId,FileName=file.Name,ContentType=file.Type,Content=file.Bytes};db.ExternalCredentials.Add(e);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return StatusCode(201,new{e.Id});
    }
    [HttpPost("credentials/{id:int}/decision"),Authorize(Roles=Roles.Reviewers)]
    public async Task<IActionResult> ReviewCredential(int id,ApprovalDecision r,CancellationToken ct){var e=await db.ExternalCredentials.FindAsync([id],ct)??throw new ApiException(404,"找不到證照。");await access.ReviewEmployee(e.EmployeeId,ct);DataScope.CheckVersion(e.RowVersion,r.RowVersion);if(e.Status!="Pending")throw new ApiException(409,"證照已處理。");e.Status=r.Approve?"Approved":"Rejected";e.ReviewerId=scope.UserId;e.ReviewedAt=DateTime.UtcNow;e.Comment=r.Comment.Trim();await training.NotifyEmployee(e.EmployeeId,"外部證照審核完成",r.Approve?"您的證照已通過核對。":"您的證照需補正，請查看審核意見。",$"credential:{e.Id}",ct);await db.SaveChangesAsync(ct);return NoContent();}
    [HttpPost("credentials/{id:int}/revoke"),Authorize(Roles=Roles.Administrators)]
    public async Task<IActionResult> Revoke(int id,ApprovalDecision r,CancellationToken ct){var e=await db.ExternalCredentials.FindAsync([id],ct)??throw new ApiException(404,"找不到證照。");DataScope.CheckVersion(e.RowVersion,r.RowVersion);if(e.Status!="Approved"||string.IsNullOrWhiteSpace(r.Comment))throw new ApiException(400,"僅可撤銷已核准證照，且須提供原因。");e.Status="Revoked";e.ReviewerId=scope.UserId;e.Comment=r.Comment.Trim();e.ReviewedAt=DateTime.UtcNow;await db.SaveChangesAsync(ct);return NoContent();}
    [HttpGet("credentials/{id:int}/download")]
    public async Task<IActionResult> File(int id,CancellationToken ct){var people=await access.People(ct);var e=await db.ExternalCredentials.SingleOrDefaultAsync(x=>x.Id==id&&people.Any(p=>p.Id==x.EmployeeId),ct)??throw new ApiException(404,"找不到證照附件。");return File(e.Content,e.ContentType,e.FileName);}
    [HttpGet("credential-compliance")]
    public async Task<IActionResult> Compliance(CancellationToken ct){var people=await(await access.People(ct)).OrderBy(x=>x.Id).Take(1000).ToListAsync(ct);var ids=people.Select(x=>x.Id).ToArray();var requirements=await db.CredentialRequirements.Include(x=>x.CredentialType).ToListAsync(ct);var valid=await db.ExternalCredentials.Where(x=>ids.Contains(x.EmployeeId)&&x.Status=="Approved"&&(x.ExpiresAt==null||x.ExpiresAt>DateTime.UtcNow)).OrderByDescending(x=>x.ExpiresAt==null).ThenByDescending(x=>x.ExpiresAt).ToListAsync(ct);return Ok(people.SelectMany(p=>requirements.Where(r=>r.JobTitle==p.JobTitle).Select(r=>{var c=valid.FirstOrDefault(x=>x.EmployeeId==p.Id&&x.CredentialTypeId==r.CredentialTypeId);return new{employeeId=p.Id,employeeName=p.Name,p.Department,r.CredentialTypeId,name=r.CredentialType.Name,status=c is null?"Missing":c.ExpiresAt<=DateTime.UtcNow.AddDays(r.RenewalNoticeDays)?"Expiring":"Valid",expiresAt=c?.ExpiresAt};})));}
}
