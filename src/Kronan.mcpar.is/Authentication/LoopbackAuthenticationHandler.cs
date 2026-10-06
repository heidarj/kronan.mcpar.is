using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Kronan.McparIs.Authentication;

// Registered only for an explicitly enabled Development process bound to loopback.
public sealed class LoopbackAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> schemes,
    ILoggerFactory logger, UrlEncoder encoder, IOptions<Options.AuthenticationOptions> auth)
    : AuthenticationHandler<AuthenticationSchemeOptions>(schemes, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Context.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip))
            return Task.FromResult(AuthenticateResult.Fail("Local development requires a loopback connection."));
        var identity = new ClaimsIdentity([
            new Claim(auth.Value.SubjectClaim, "local-development"),
            new Claim("scope", string.Join(' ', auth.Value.Scopes))], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
