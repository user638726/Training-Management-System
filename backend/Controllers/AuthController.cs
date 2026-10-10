using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using TrainingManagement.Api.Data;
using TrainingManagement.Api.Infrastructure;
using TrainingManagement.Api.Models;

namespace TrainingManagement.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(TrainingDbContext db, IPasswordHasher<AppUser> hasher,
    DummyPasswordHash dummyHash, JwtTokenService tokens) : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("register")]
    public async Task<ActionResult<UserResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var normalized = request.Username.ToUpperInvariant();
        if (await db.AppUsers.AnyAsync(x => x.NormalizedUsername == normalized, cancellationToken))
            throw new ApiException(409, "此帳號已存在，請使用其他帳號。");
        var user = new AppUser
        {
            Username = request.Username, NormalizedUsername = normalized,
            DisplayName = request.DisplayName, Role = "Employee", IsActive = true, TokenVersion = 0
        };
        user.PasswordHash = hasher.HashPassword(user, request.Password);
        db.AppUsers.Add(user);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // The database unique index is authoritative when two anonymous
            // requests race after the preliminary availability check.
            throw new ApiException(409, "此帳號已存在，請使用其他帳號。");
        }
        return StatusCode(StatusCodes.Status201Created, UserResponse.From(user));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var normalized = request.Username.Trim().ToUpperInvariant();
        var user = await db.AppUsers.SingleOrDefaultAsync(x => x.NormalizedUsername == normalized, cancellationToken);
        var verification = hasher.VerifyHashedPassword(user ?? new AppUser(), user?.PasswordHash ?? dummyHash.Value, request.Password);
        if (user is null || !user.IsActive || verification == PasswordVerificationResult.Failed)
        {
            Response.Headers.WWWAuthenticate = "Bearer";
            throw new ApiException(401, "帳號或密碼不正確。");
        }
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, request.Password);
            await db.SaveChangesAsync(cancellationToken);
        }
        return Ok(tokens.Issue(user));
    }

    [HttpGet("me")]
    public async Task<ActionResult<UserResponse>> Me(CancellationToken cancellationToken)
    {
        var id = int.Parse(User.FindFirstValue("sub")!);
        var user = await db.AppUsers.AsNoTracking().SingleAsync(x => x.Id == id, cancellationToken);
        return Ok(UserResponse.From(user));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var id = int.Parse(User.FindFirstValue("sub")!);
        await db.AppUsers.Where(x => x.Id == id).ExecuteUpdateAsync(
            updates => updates.SetProperty(x => x.TokenVersion, x => x.TokenVersion + 1), cancellationToken);
        return NoContent();
    }
}
