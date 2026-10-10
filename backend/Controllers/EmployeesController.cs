using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using TrainingManagement.Api.Data;
using TrainingManagement.Api.Infrastructure;
using TrainingManagement.Api.Models;

namespace TrainingManagement.Api.Controllers;

[ApiController]
[Route("api/employees")]
public sealed class EmployeesController(TrainingDbContext db, MutationGate gate) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EmployeeResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var employees = await db.Employees.AsNoTracking().OrderBy(x => x.EmployeeNumber).ToListAsync(cancellationToken);
        return Ok(employees.Select(ToResponse).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<EmployeeResponse>> Get(int id, CancellationToken cancellationToken)
    {
        var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (employee is null) throw new ApiException(404, "找不到此員工。");
        return Ok(ToResponse(employee));
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
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
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<EmployeeResponse>> Update(int id, EmployeeRequest request, CancellationToken cancellationToken)
    {
        using var write = await gate.EnterAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var employee = await db.Employees.FindAsync([id], cancellationToken);
        if (employee is null) throw new ApiException(404, "找不到此員工。");
        await EnsureUnique(request, id, cancellationToken);
        Apply(employee, request);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(ToResponse(employee));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
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
    }

    private static EmployeeResponse ToResponse(Employee employee) => new(employee.Id, employee.EmployeeNumber,
        employee.Name, employee.Email, employee.Department, employee.JobTitle, employee.IsActive);
}
