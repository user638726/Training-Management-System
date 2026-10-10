using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TrainingManagement.Api.Data;
using TrainingManagement.Api.Models;

namespace TrainingManagement.Api.Infrastructure;

public sealed record JwtSettings(string Issuer, string Audience, string SigningKey, int ExpiresMinutes)
{
    public static JwtSettings Load(IConfiguration configuration)
    {
        var key = configuration["Jwt:SigningKey"];
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32)
            throw new InvalidOperationException("Jwt:SigningKey 必須透過安全設定提供，且至少包含 32 bytes；系統不提供預設金鑰。");
        var issuer = configuration["Jwt:Issuer"] ?? "TrainingManagement.Api";
        var audience = configuration["Jwt:Audience"] ?? "TrainingManagement.Web";
        var minutes = configuration.GetValue("Jwt:ExpiresMinutes", 30);
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(audience) || minutes is < 1 or > 120)
            throw new InvalidOperationException("JWT issuer、audience 必須非空白，期限必須介於 1 與 120 分鐘。");
        return new(issuer, audience, key, minutes);
    }
}

public static class JwtAuthentication
{
    public static void AddTrainingAuthentication(this IServiceCollection services, JwtSettings settings)
    {
        services.AddSingleton(settings);
        services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
        services.AddSingleton<DummyPasswordHash>();
        services.AddSingleton<JwtTokenService>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.IncludeErrorDetails = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = settings.Issuer,
                ValidateAudience = true, ValidAudience = settings.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)),
                ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                ClockSkew = TimeSpan.Zero, NameClaimType = "name", RoleClaimType = "role"
            };
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = async context =>
                {
                    if (!int.TryParse(context.Principal?.FindFirstValue("sub"), out var id)
                        || !int.TryParse(context.Principal?.FindFirstValue("token_version"), out var version))
                    {
                        context.Fail("無效的身分驗證。");
                        return;
                    }
                    var db = context.HttpContext.RequestServices.GetRequiredService<TrainingDbContext>();
                    var user = await db.AppUsers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id,
                        context.HttpContext.RequestAborted);
                    if (user is null || !user.IsActive || user.TokenVersion != version)
                        context.Fail("身分驗證已失效。");
                }
            };
        });
        services.AddAuthorizationBuilder().SetFallbackPolicy(new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser().Build());
    }
}

public sealed class DummyPasswordHash(IPasswordHasher<AppUser> hasher)
{
    // Unknown users still perform a password verification to avoid a trivial
    // username-dependent timing difference. No usable account is created.
    public string Value { get; } = hasher.HashPassword(new AppUser(), Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
}

public sealed class JwtTokenService(JwtSettings settings)
{
    public LoginResponse Issue(AppUser user)
    {
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(settings.ExpiresMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString(CultureInfo.InvariantCulture)),
            new Claim("name", user.Username), new Claim("role", user.Role),
            new Claim("token_version", user.TokenVersion.ToString(CultureInfo.InvariantCulture)),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new Claim(JwtRegisteredClaimNames.Iat, new DateTimeOffset(now).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64)
        };
        var token = new JwtSecurityToken(settings.Issuer, settings.Audience, claims, now, expires,
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)),
                SecurityAlgorithms.HmacSha256));
        return new(new JwtSecurityTokenHandler().WriteToken(token), "Bearer", expires, UserResponse.From(user));
    }
}
