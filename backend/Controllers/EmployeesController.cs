using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using TrainingManagement.Api.Data;
using TrainingManagement.Api.Infrastructure;
using TrainingManagement.Api.Models;

namespace TrainingManagement.Api.Controllers;

[ApiController]
[Route("api/employees")]
public sealed class EmployeesController(TrainingDbContext db, MutationGate gate, DataScope scope) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EmployeeResponse>>> GetAll([FromQuery] ListQuery filter, CancellationToken cancellationToken)
    {
        var query = (await scope.Employees(cancellationToken)).AsNoTracking();
        if (!string.IsNullOrWhiteSpace(filter.Search)) { var q = filter.Search.Trim(); query = query.Where(x => x.Name.Contains(q) || x.EmployeeNumber.Contains(q) || x.Email.Contains(q) || x.JobTitle.Contains(q)); }
        if (!string.IsNullOrWhiteSpace(filter.Department)) query = query.Where(x => x.Department == filter.Department);
        if (filter.IsActive.HasValue) query = query.Where(x => x.IsActive == filter.IsActive.Value);
        Response.Headers["X-Total-Count"] = (await query.CountAsync(cancellationToken)).ToString();
        var employees = await query.OrderBy(x => x.EmployeeNumber).ThenBy(x => x.Id).Skip(filter.Offset).Take(filter.Limit).ToListAsync(cancellationToken);
        return Ok(employees.Select(ToResponse).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<EmployeeResponse>> Get(int id, CancellationToken cancellationToken)
    {
        var employee = await (await scope.Employees(cancellationToken)).AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (employee is null) throw new ApiException(404, "找不到此員工。");
        return Ok(ToResponse(employee));
    }

    [HttpPost]
    [Authorize(Roles = Roles.Administrators)]
    public async Task<ActionResult<EmployeeResponse>> Create(EmployeeRequest request, CancellationToken cancellationToken)
    {
        using var write = await gate.EnterAsync(cancellationToken);
        await EnsureUnique(request, null, cancellationToken);
        var employee = new Employee();
        Apply(employee, request);
        db.Employees.Add(employee);
        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = employee.Id }, ToResponse(employee));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = Roles.Administrators)]
    public async Task<ActionResult<EmployeeResponse>> Update(int id, EmployeeRequest request, CancellationToken cancellationToken)
    {
        using var write = await gate.EnterAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var employee = await db.Employees.FindAsync([id], cancellationToken);
        if (employee is null) throw new ApiException(404, "找不到此員工。");
        DataScope.CheckVersion(employee.RowVersion, request.RowVersion);
        await EnsureUnique(request, id, cancellationToken);
        Apply(employee, request);
        if (!employee.IsActive) {
            foreach (var account in await db.AppUsers.Where(x => x.EmployeeId == id).ToListAsync(cancellationToken)) {
                if (account.Id == scope.UserId) throw new ApiException(409, "不可透過員工停用目前登入的帳號。");
                if (account.Role == "Admin" && (!User.IsInRole("Admin") || await db.AppUsers.CountAsync(x => x.Role == "Admin" && x.IsActive && x.IsApproved, cancellationToken) <= 1))
                    throw new ApiException(409, "不可停用此管理員帳號，請由管理員確認帳號權限與備援管理員。");
                account.IsActive = false; account.TokenVersion++;
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(ToResponse(employee));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = Roles.Administrators)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        using var write = await gate.EnterAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var employee = await db.Employees.FindAsync([id], cancellationToken);
        if (employee is null) throw new ApiException(404, "找不到此員工。");
        if (await db.Enrollments.AnyAsync(x => x.EmployeeId == id, cancellationToken))
            throw new ApiException(409, "此員工已有訓練紀錄，請改為停用員工以保留歷史資料。");
        if (await db.AppUsers.AnyAsync(x => x.EmployeeId == id, cancellationToken))
            throw new ApiException(409, "此員工有對應的登入帳號，請保留或停用員工資料。");
        db.Employees.Remove(employee);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    private async Task EnsureUnique(EmployeeRequest request, int? id, CancellationToken cancellationToken)
    {
        var number = request.EmployeeNumber.Trim().ToUpperInvariant();
        var email = request.Email.Trim().ToLowerInvariant();
        if (await db.Employees.AnyAsync(x => x.Id != id && x.EmployeeNumber == number, cancellationToken))
            throw new ApiException(409, "此員工編號已存在。");
        if (await db.Employees.AnyAsync(x => x.Id != id && x.Email == email, cancellationToken))
            throw new ApiException(409, "此電子郵件已被其他員工使用。");
    }

    private static void Apply(Employee employee, EmployeeRequest request)
    {
        employee.EmployeeNumber = request.EmployeeNumber.Trim().ToUpperInvariant();
        employee.Name = request.Name.Trim();
        employee.Email = request.Email.Trim().ToLowerInvariant();
        employee.Department = request.Department.Trim();
        employee.JobTitle = request.JobTitle.Trim();
        employee.IsActive = request.IsActive;
        if (request.HiredAt.HasValue) employee.HiredAt = request.HiredAt.Value.Date;
    }

    private static EmployeeResponse ToResponse(Employee employee) => new(employee.Id, employee.EmployeeNumber,
        employee.Name, employee.Email, employee.Department, employee.JobTitle, employee.IsActive, Convert.ToBase64String(employee.RowVersion), employee.HiredAt);
}
