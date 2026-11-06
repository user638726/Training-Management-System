using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using TrainingManagement.Api.Data;
using TrainingManagement.Api.Infrastructure;
using TrainingManagement.Api.Models;

namespace TrainingManagement.Api.Controllers;

[ApiController, Route("api/auth/sso"), AllowAnonymous]
public sealed class SsoController(IConfiguration config, IWebHostEnvironment environment, IDataProtectionProvider protection,
    IHttpClientFactory clients, TrainingDbContext db, JwtTokenService tokens) : ControllerBase
{
    private const string CookieName = "training.sso.state";
    private sealed record State(string Nonce, string Verifier, DateTime ExpiresAt);
    public sealed record RedeemRequest(string Code);
    [HttpGet("config")]
    public IActionResult Config() => Ok(new { enabled = config.GetValue("Sso:Enabled", false), label = config["Sso:Label"] ?? "公司帳號登入" });
    private string Origin()
    {
        if (!config.GetValue("Sso:Enabled", false)) throw new ApiException(409, "企業 SSO 尚未啟用。");
        var value = config["Sso:PublicOrigin"]?.TrimEnd('/');
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !ValidUri(uri) || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ApiException(503, "SSO PublicOrigin 設定不正確。");
        return value!;
    }
    private bool ValidUri(Uri uri) => uri.Scheme == "https" || (environment.IsDevelopment() && uri.IsLoopback && uri.Scheme == "http");
    private async Task<OpenIdConnectConfiguration> Discovery(CancellationToken ct)
    {
        var authority = config["Sso:Authority"]?.TrimEnd('/');
        if (!Uri.TryCreate(authority, UriKind.Absolute, out var uri) || !ValidUri(uri)) throw new ApiException(503, "SSO Authority 設定不正確。");
        var retriever = new HttpDocumentRetriever(clients.CreateClient("integrations")) { RequireHttps = !environment.IsDevelopment() };
        var result = await OpenIdConnectConfigurationRetriever.GetAsync(authority + "/.well-known/openid-configuration", retriever, ct);
        if (!Uri.TryCreate(result.AuthorizationEndpoint, UriKind.Absolute, out var auth) || !ValidUri(auth)
            || !Uri.TryCreate(result.TokenEndpoint, UriKind.Absolute, out var token) || !ValidUri(token)) throw new ApiException(503, "SSO 中繼資料端點不正確。");
        return result;
    }
    [HttpGet("start"), EnableRateLimiting("login")]
    public async Task<IActionResult> Start(CancellationToken ct)
    {
        var origin = Origin(); var discovery = await Discovery(ct);
        var clientId = config["Sso:ClientId"];
        if (string.IsNullOrWhiteSpace(clientId)) throw new ApiException(503, "SSO ClientId 尚未設定。");
        var state = new State(Random(), Random(), DateTime.UtcNow.AddMinutes(5));
        var protectedState = protection.CreateProtector("training.sso.v1").Protect(JsonSerializer.Serialize(state));
        Response.Cookies.Append(CookieName, protectedState, new CookieOptions { HttpOnly = true, Secure = origin.StartsWith("https://", StringComparison.Ordinal),
            SameSite = SameSiteMode.Lax, Path = "/api/auth/sso", MaxAge = TimeSpan.FromMinutes(5), IsEssential = true });
        var parameters = new Dictionary<string, string> { ["client_id"] = clientId, ["redirect_uri"] = origin + "/api/auth/sso/callback", ["response_type"] = "code",
            ["scope"] = "openid profile email", ["state"] = protectedState, ["nonce"] = state.Nonce,
            ["code_challenge"] = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(state.Verifier))), ["code_challenge_method"] = "S256" };
        return Redirect(discovery.AuthorizationEndpoint + "?" + string.Join("&", parameters.Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value))));
    }
    [HttpGet("callback"), EnableRateLimiting("login")]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state, CancellationToken ct)
    {
        var origin = Origin(); Response.Headers.CacheControl = "no-store";
        if (string.IsNullOrWhiteSpace(code) || code.Length > 8000 || string.IsNullOrWhiteSpace(state) || state.Length > 8000
            || !Request.Cookies.TryGetValue(CookieName, out var cookie) || cookie != state) return Redirect(origin + "/login?ssoError=invalid-state");
        Response.Cookies.Delete(CookieName, new CookieOptions { Path = "/api/auth/sso" });
        try {
            var payload = JsonSerializer.Deserialize<State>(protection.CreateProtector("training.sso.v1").Unprotect(state));
            if (payload is null || payload.ExpiresAt <= DateTime.UtcNow) return Redirect(origin + "/login?ssoError=expired");
            var discovery = await Discovery(ct);
            var values = new Dictionary<string, string> { ["grant_type"] = "authorization_code", ["code"] = code,
                ["client_id"] = config["Sso:ClientId"]!, ["redirect_uri"] = origin + "/api/auth/sso/callback", ["code_verifier"] = payload.Verifier };
            if (!string.IsNullOrWhiteSpace(config["Sso:ClientSecret"])) values["client_secret"] = config["Sso:ClientSecret"]!;
            using var response = await clients.CreateClient("integrations").PostAsync(discovery.TokenEndpoint, new FormUrlEncodedContent(values), ct);
            if (!response.IsSuccessStatusCode) return Redirect(origin + "/login?ssoError=provider");
            using var tokenJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var idToken = tokenJson.RootElement.GetProperty("id_token").GetString();
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(idToken, new TokenValidationParameters { ValidateIssuer = true, ValidIssuer = discovery.Issuer,
                ValidateAudience = true, ValidAudience = config["Sso:ClientId"], ValidateIssuerSigningKey = true, IssuerSigningKeys = discovery.SigningKeys,
                ValidateLifetime = true, RequireSignedTokens = true, RequireExpirationTime = true, ClockSkew = TimeSpan.FromMinutes(1),
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256, SecurityAlgorithms.RsaSha384, SecurityAlgorithms.RsaSha512, SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.EcdsaSha384] }, out _);
            if (principal.FindFirst("nonce")?.Value != payload.Nonce) return Redirect(origin + "/login?ssoError=nonce");
            var subject = principal.FindFirst("sub")?.Value;
            var user = await db.AppUsers.SingleOrDefaultAsync(x => x.ExternalSubject == subject && x.ExternalIssuer == discovery.Issuer && x.IsActive && x.IsApproved, ct);
            if (string.IsNullOrWhiteSpace(subject) || user is null) return Redirect(origin + "/login?ssoError=unlinked");
            if (user.EmployeeId.HasValue && !await db.Employees.AnyAsync(x => x.Id == user.EmployeeId && x.IsActive, ct)) return Redirect(origin + "/login?ssoError=disabled");
            var ticket = Random(); db.SsoTickets.Add(new SsoTicket { Hash = GovernanceController.Hash(ticket), UserId = user.Id, ExpiresAt = DateTime.UtcNow.AddMinutes(1) });
            await db.SaveChangesAsync(ct); return Redirect(origin + "/login#ssoCode=" + ticket);
        } catch (Exception ex) when (ex is SecurityTokenException or CryptographicException or JsonException or ArgumentException or InvalidOperationException) {
            return Redirect(origin + "/login?ssoError=verification");
        }
    }
    [HttpPost("redeem"), EnableRateLimiting("login")]
    public async Task<IActionResult> Redeem(RedeemRequest r, CancellationToken ct)
    {
        _ = Origin(); Response.Headers.CacheControl = "no-store";
        if (string.IsNullOrWhiteSpace(r.Code) || r.Code.Length > 200) throw new ApiException(400, "登入交換碼無效。");
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var hash = GovernanceController.Hash(r.Code);
        var ticket = await db.SsoTickets.SingleOrDefaultAsync(x => x.Hash == hash && x.ExpiresAt > DateTime.UtcNow, ct) ?? throw new ApiException(401, "登入交換碼已使用或已到期。");
        var user = await db.AppUsers.SingleOrDefaultAsync(x => x.Id == ticket.UserId && x.IsActive && x.IsApproved, ct) ?? throw new ApiException(401, "帳號已停用。");
        db.SsoTickets.Remove(ticket); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Ok(tokens.Issue(user));
    }
    private static string Random() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
}
