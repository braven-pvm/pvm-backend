using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Pvm.Api.Auth;

public sealed class DevelopmentAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DevelopmentBypass";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var email = Request.Headers["X-PVM-Dev-User-Email"].FirstOrDefault()
            ?? "developer@pvm.co.za";
        var objectId = Request.Headers["X-PVM-Dev-User-ObjectId"].FirstOrDefault()
            ?? "35425387-d19a-4e63-97b5-2165cce0032b";
        var displayName = Request.Headers["X-PVM-Dev-User-Name"].FirstOrDefault()
            ?? "Marius Bloemhof";

        var claims = new List<Claim>
        {
            new("oid", objectId),
            new("preferred_username", email),
            new(ClaimTypes.Email, email),
            new("name", displayName)
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
