using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TrainingManagement.Api.Data;
using TrainingManagement.Api.Infrastructure;
using TrainingManagement.Api.Models;

namespace TrainingManagement.Api.Controllers;

[ApiController, Route("api/enterprise")]
public sealed class GovernanceController(TrainingDbContext db, DataScope scope, MutationGate gate,
    IPasswordHasher<AppUser> hasher, IConfiguration config, TrainingService training) : ControllerBase
{
    [HttpGet("facets")]
    public async Task<IActionResult> Facets(CancellationToken ct) => Ok(new {
        categories = await scope.Courses().Select(x => x.Category).Distinct().OrderBy(x => x).ToListAsync(ct),
        departments = await (await scope.Employees(ct)).Select(x => x.Department).Distinct().OrderBy(x => x).ToListAsync(ct)
    });
    [HttpGet("staff-directory"), Authorize(Roles = Roles.Administrators)]
    public async Task<IActionResult> StaffDirectory(CancellationToken ct) => Ok(await db.AppUsers.AsNoTracking()
        .Where(x => x.IsActive && x.IsApproved && (x.Role == "Manager" || x.Role == "Instructor"))
        .Select(x => new { x.Id, x.DisplayName, x.Role }).ToListAsync(ct));
    [HttpGet("departments")]
    public async Task<IActionResult> Departments(CancellationToken ct)
    {
        var allowed = (await scope.Employees(ct)).Select(x => x.Department);
        return Ok(await db.Departments.AsNoTracking().Where(x => scope.IsGlobal || allowed.Contains(x.Name))
            .OrderBy(x => x.Name).Select(x => new { x.Id, x.Name, x.ParentId, x.ManagerUserId, rowVersion = Convert.ToBase64String(x.RowVersion) }).ToListAsync(ct));
    }
    [HttpPost("departments"), Authorize(Roles = Roles.Administrators)]
    public async Task<IActionResult> CreateDepartment(DepartmentRequest request, CancellationToken ct)
    {
        using var write = await gate.EnterAsync(ct);
        await ValidateDepartment(request, null, ct);
        var d = new Department { Name = request.Name.Trim(), ParentId = request.ParentId, ManagerUserId = request.ManagerUserId };
        db.Departments.Add(d); await db.SaveChangesAsync(ct);
        return Created($"/api/enterprise/departments/{d.Id}", new { d.Id });
    }
    [HttpPut("departments/{id:int}"), Authorize(Roles = Roles.Administrators)]
    public async Task<IActionResult> UpdateDepartment(int id, DepartmentRequest request, CancellationToken ct)
    {
        using var write = await gate.EnterAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var d = await db.Departments.FindAsync([id], ct) ?? throw new ApiException(404, "找不到部門。");
        DataScope.CheckVersion(d.RowVersion, request.RowVersion);
        await ValidateDepartment(request, id, ct);
        if (d.Name != request.Name.Trim() && await db.Employees.AnyAsync(x => x.Department == d.Name, ct))
            throw new ApiException(409, "此部門已有員工，請先調整員工部門再更名。");
        d.Name = request.Name.Trim(); d.ParentId = request.ParentId; d.ManagerUserId = request.ManagerUserId;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return NoContent();
    }
    private async Task ValidateDepartment(DepartmentRequest r, int? id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) throw new ApiException(400, "部門名稱不可空白。");
        if (await db.Departments.AnyAsync(x => x.Name == r.Name.Trim() && x.Id != id, ct)) throw new ApiException(409, "部門名稱已存在。");
        if (r.ManagerUserId.HasValue && !await db.AppUsers.AnyAsync(x => x.Id == r.ManagerUserId && x.Role == "Manager" && x.IsActive && x.IsApproved, ct))
            throw new ApiException(400, "請選擇已核准且啟用的主管帳號。");
        var parent = r.ParentId; var seen = new HashSet<int>();
        while (parent.HasValue) {
            if (parent == id || !seen.Add(parent.Value)) throw new ApiException(400, "部門階層不可形成循環。");
            var p = await db.Departments.FindAsync([parent.Value], ct) ?? throw new ApiException(400, "上層部門不存在。");
            parent = p.ParentId;
        }
    }
    [HttpGet("accounts"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> Accounts(CancellationToken ct) => Ok(await db.AppUsers.AsNoTracking().OrderBy(x => x.Username)
        .Select(x => new { x.Id, x.Username, x.DisplayName, x.Role, x.IsActive, x.IsApproved, x.EmployeeId, x.ExternalSubject,
            rowVersion = Convert.ToBase64String(x.RowVersion) }).ToListAsync(ct));

    [HttpPut("accounts/{id:int}"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateAccount(int id, AccountUpdateRequest r, CancellationToken ct)
    {
        if (!Roles.All.Contains(r.Role)) throw new ApiException(400, "無效的角色。");
        using var write = await gate.EnterAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var u = await db.AppUsers.FindAsync([id], ct) ?? throw new ApiException(404, "找不到帳號。");
        DataScope.CheckVersion(u.RowVersion, r.RowVersion);
        if (u.Id == scope.UserId && (!r.IsActive || !r.IsApproved || r.Role != "Admin")) throw new ApiException(409, "不可停用或降級目前登入的管理員。");
        if (u.Role == "Admin" && (!r.IsActive || !r.IsApproved || r.Role != "Admin") &&
            await db.AppUsers.CountAsync(x => x.Role == "Admin" && x.IsActive && x.IsApproved, ct) <= 1)
            throw new ApiException(409, "至少須保留一名啟用的管理員。");
        if (r.EmployeeId.HasValue && (!await db.Employees.AnyAsync(x => x.Id == r.EmployeeId && x.IsActive, ct)
            || await db.AppUsers.AnyAsync(x => x.Id != id && x.EmployeeId == r.EmployeeId, ct)))
            throw new ApiException(409, "員工不存在、已停用或已綁定其他帳號。");
        u.Role = r.Role; u.IsActive = r.IsActive; u.IsApproved = r.IsApproved; u.EmployeeId = r.EmployeeId;
        u.ExternalSubject = string.IsNullOrWhiteSpace(r.ExternalSubject) ? null : r.ExternalSubject.Trim(); u.TokenVersion++;
        u.ExternalIssuer = u.ExternalSubject is null ? null : (config["Sso:Issuer"] ?? config["Sso:Authority"])?.TrimEnd('/');
        if (u.ExternalSubject is not null && string.IsNullOrWhiteSpace(u.ExternalIssuer)) throw new ApiException(409, "請先設定 Sso:Authority 或 Sso:Issuer，再綁定企業身分。");
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Ok(UserResponse.From(u));
    }
    [HttpPost("invitations"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> Invite(InvitationRequest r, CancellationToken ct)
    {
        if (!Roles.All.Contains(r.Role) || r.Role == "Admin") throw new ApiException(400, "邀請角色不可為管理員。");
        if (r.EmployeeId.HasValue && (!await db.Employees.AnyAsync(x => x.Id == r.EmployeeId && x.Email == r.Email.Trim().ToLower(), ct)
            || await db.AppUsers.AnyAsync(x => x.EmployeeId == r.EmployeeId, ct))) throw new ApiException(409, "員工電子郵件不符或已有帳號。");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var invite = new AccountInvitation { Email = r.Email.Trim().ToLowerInvariant(), Role = r.Role,
            EmployeeId = r.EmployeeId, TokenHash = Hash(token), ExpiresAt = DateTime.UtcNow.AddDays(3) };
        db.Invitations.Add(invite); await db.SaveChangesAsync(ct);
        return Ok(new { token, invite.ExpiresAt, registrationPath = $"/invite#token={token}" });
    }
    [HttpPost("invitations/accept"), AllowAnonymous, EnableRateLimiting("register")]
    public async Task<IActionResult> Accept(AcceptInvitationRequest r, CancellationToken ct)
    {
        using var write = await gate.EnterAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var hash = Hash(r.Token);
        var invitation = await db.Invitations.SingleOrDefaultAsync(x => x.TokenHash == hash && x.AcceptedAt == null && x.ExpiresAt > DateTime.UtcNow, ct)
            ?? throw new ApiException(400, "邀請不存在、已使用或已到期。");
        var username = r.Username.Trim();
        if (string.IsNullOrWhiteSpace(r.DisplayName) || await db.AppUsers.AnyAsync(x => x.NormalizedUsername == username.ToUpper(), ct))
            throw new ApiException(409, "姓名不可空白，帳號不可重複。");
        if (invitation.EmployeeId.HasValue && (!await db.Employees.AnyAsync(x => x.Id == invitation.EmployeeId && x.IsActive, ct)
            || await db.AppUsers.AnyAsync(x => x.EmployeeId == invitation.EmployeeId, ct))) throw new ApiException(409, "邀請的員工已停用或已有帳號。");
        var u = new AppUser { Username = username, NormalizedUsername = username.ToUpperInvariant(), DisplayName = r.DisplayName.Trim(),
            Role = invitation.Role, EmployeeId = invitation.EmployeeId, IsApproved = true, IsActive = true };
        u.PasswordHash = hasher.HashPassword(u, r.Password); invitation.AcceptedAt = DateTime.UtcNow;
        db.AppUsers.Add(u); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return StatusCode(201, UserResponse.From(u));
    }
    [HttpPost("password")]
    public async Task<IActionResult> Password(PasswordChangeRequest r, CancellationToken ct)
    {
        var user = await db.AppUsers.FindAsync([scope.UserId], ct) ?? throw new ApiException(401, "請重新登入。");
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, r.CurrentPassword) == PasswordVerificationResult.Failed)
            throw new ApiException(400, "目前密碼不正確。");
        user.PasswordHash = hasher.HashPassword(user, r.NewPassword); user.TokenVersion++;
        await db.SaveChangesAsync(ct); return NoContent();
    }
    [HttpGet("audit"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> Audit([FromQuery] string? entity, [FromQuery] string? action,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 30, CancellationToken ct = default)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.AuditEvents.AsNoTracking().Where(x => (entity == null || x.EntityType == entity) && (action == null || x.Action == action));
        return Ok(new { total = await query.CountAsync(ct), page, pageSize,
            items = await query.OrderByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct) });
    }
    [HttpGet("integrations"), Authorize(Roles = "Admin")]
    public IActionResult Integrations() => Ok(new {
        sso = new { enabled = config.GetValue("Sso:Enabled", false), configured = !string.IsNullOrWhiteSpace(config["Sso:Authority"]),
            authority = config["Sso:Authority"], clientId = config["Sso:ClientId"], callbackPath = "/api/auth/sso/callback" },
        mail = new { enabled = config.GetValue("Mail:Enabled", false), configured = !string.IsNullOrWhiteSpace(config["Mail:Host"]),
            host = config["Mail:Host"], from = config["Mail:From"] },
        hr = new { configured = !string.IsNullOrWhiteSpace(config["Hr:EmployeesEndpoint"]) },
        registrationApproval = config.GetValue("Enterprise:RequireRegistrationApproval", true)
    });
    [HttpPut("courses/{id:int}/settings"), Authorize(Roles = Roles.Administrators)]
    public async Task<IActionResult> CourseSettings(int id, CourseSettingsRequest r, CancellationToken ct)
    {
        using var write = await gate.EnterAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await CourseWriteLock.AcquireAsync(db, id, ct);
        var c = await db.Courses.FindAsync([id], ct) ?? throw new ApiException(404, "找不到課程。");
        DataScope.CheckVersion(c.RowVersion, r.RowVersion);
        if (r.InstructorUserId.HasValue && !await db.AppUsers.AnyAsync(x => x.Id == r.InstructorUserId && x.Role == "Instructor" && x.IsActive, ct))
            throw new ApiException(400, "請指定啟用的講師帳號。");
        var current = r.PrerequisiteCourseId; var seen = new HashSet<int> { id };
        while (current.HasValue) {
            if (!seen.Add(current.Value)) throw new ApiException(400, "先修課程不可形成循環。");
            var prerequisite = await db.Courses.FindAsync([current.Value], ct) ?? throw new ApiException(400, "先修課程不存在。");
            current = prerequisite.PrerequisiteCourseId;
        }
        c.InstructorUserId = r.InstructorUserId; c.RequiresApproval = r.RequiresApproval;
        c.RequiresAttendance = r.RequiresAttendance; c.RequiresQuiz = r.RequiresQuiz; c.PassScore = r.PassScore;
        c.Cost = r.Cost; c.PrerequisiteCourseId = r.PrerequisiteCourseId; c.CertificateValidityMonths = r.CertificateValidityMonths;
        c.Revision++;
        foreach (var eid in await db.Enrollments.Where(x => x.CourseId == id && x.Status != EnrollmentStatus.Cancelled).Select(x => x.EmployeeId).ToListAsync(ct))
            await training.NotifyEmployee(eid, "完訓條件已更新", $"「{c.Title}」的課程條件已有異動，請重新確認。", $"settings:{id}:{c.Revision}:{eid}", ct);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return NoContent();
    }
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
