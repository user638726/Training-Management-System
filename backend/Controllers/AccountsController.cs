using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrainingManagement.Api.Data;
using TrainingManagement.Api.Infrastructure;
using TrainingManagement.Api.Models;

namespace TrainingManagement.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/accounts")]
public sealed class AccountsController(TrainingDbContext db, MutationGate gate) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var accounts = await db.AppUsers.AsNoTracking().Where(x => x.Role == "Employee")
            .OrderBy(x => x.Username).ToListAsync(cancellationToken);
        return Ok(accounts.Select(UserResponse.From).ToList());
    }

    [HttpPost("{id:int}/employee")]
    public async Task<ActionResult<EmployeeResponse>> CreateEmployee(int id, EmployeeRequest request,
        CancellationToken cancellationToken)
    {
        using var write = await gate.EnterAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var account = await db.AppUsers.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == id && x.Role == "Employee", cancellationToken);
        if (account is null) throw new ApiException(404, "找不到此一般使用者帳號。");
        if (!account.IsActive) throw new ApiException(409, "停用的帳號無法建立員工資料。");
        if (account.EmployeeId is not null) throw new ApiException(409, "此帳號已有員工資料。");

        var number = request.EmployeeNumber.Trim().ToUpperInvariant();
        var email = request.Email.Trim().ToLowerInvariant();
        if (await db.Employees.AnyAsync(x => x.EmployeeNumber == number, cancellationToken))
            throw new ApiException(409, "此員工編號已存在。");
        if (await db.Employees.AnyAsync(x => x.Email == email, cancellationToken))
            throw new ApiException(409, "此電子郵件已被其他員工使用。");

        var employee = new Employee
        {
            EmployeeNumber = number,
            Name = request.Name.Trim(),
            Email = email,
            Department = request.Department.Trim(),
            JobTitle = request.JobTitle.Trim(),
            IsActive = request.IsActive
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync(cancellationToken);
        var linked = await db.AppUsers.Where(x => x.Id == id && x.Role == "Employee" && x.IsActive && x.EmployeeId == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.EmployeeId, employee.Id), cancellationToken);
        if (linked != 1) throw new ApiException(409, "此帳號已有員工資料，請重新整理後再試。");
        await transaction.CommitAsync(cancellationToken);

        return Created($"/api/employees/{employee.Id}", new EmployeeResponse(employee.Id,
            employee.EmployeeNumber, employee.Name, employee.Email, employee.Department,
            employee.JobTitle, employee.IsActive));
    }
}
