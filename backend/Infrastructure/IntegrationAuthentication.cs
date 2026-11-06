using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrainingManagement.Api.Data;
namespace TrainingManagement.Api.Infrastructure;
public sealed class IntegrationAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder,TrainingDbContext db)
    :AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
{
    public const string SchemeName="IntegrationKey";
    public static readonly string[] AllowedScopes=["scim.read","scim.write","learning.read","learning.write","training.read"];
    public static string Hash(string token)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token=Request.Headers["X-Api-Key"].ToString();if(string.IsNullOrEmpty(token)){var header=Request.Headers.Authorization.ToString();if(header.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase))token=header[7..].Trim();}
        if(!token.StartsWith("tm_",StringComparison.Ordinal)||token.Length>200)return AuthenticateResult.NoResult();
        var hash=Hash(token);var client=await db.IntegrationClients.AsNoTracking().SingleOrDefaultAsync(x=>x.TokenHash==hash&&x.IsActive&&x.ExpiresAt>DateTime.UtcNow,Context.RequestAborted);
        if(client is null)return AuthenticateResult.Fail("Integration credential is invalid or expired.");
        var claims=new List<Claim>{new("client_id",client.Id.ToString()),new("name",client.Name)};claims.AddRange(client.Scopes.Split(' ',StringSplitOptions.RemoveEmptyEntries).Select(s=>new Claim("client_scope",s)));
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims,SchemeName,"name","role")),SchemeName));
    }
}
