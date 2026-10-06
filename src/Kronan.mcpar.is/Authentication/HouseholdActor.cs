using System.Security.Claims;
using Kronan.McparIs.Options;
using Kronan.McparIs.Services;
using Microsoft.Extensions.Options;

namespace Kronan.McparIs.Authentication;

public sealed record HouseholdActor(string Subject);

public sealed class HouseholdAccess(IHttpContextAccessor contexts, IOptions<AuthenticationOptions> options)
{
    public static bool IsMember(ClaimsPrincipal user, AuthenticationOptions options) =>
        user.Identity?.IsAuthenticated == true &&
        options.AllowedSubjects.Contains(user.FindFirstValue(options.SubjectClaim), StringComparer.Ordinal);

    public static bool HasScope(ClaimsPrincipal user, string scope) =>
        user.FindAll("scope").Concat(user.FindAll("scp"))
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Any(s => s == scope || s == scope[(scope.LastIndexOf('/') + 1)..]);

    public HouseholdActor RequireMember()
    {
        var user = contexts.HttpContext?.User;
        if (user is null || !IsMember(user, options.Value))
            throw new ServiceFailure("forbidden", "This connected account does not have permission for this operation.");
        return new HouseholdActor(user.FindFirstValue(options.Value.SubjectClaim)!);
    }

    public HouseholdActor Require(string scope)
    {
        var actor = RequireMember();
        if (!HasScope(contexts.HttpContext!.User, scope))
            throw new ServiceFailure("forbidden", "The connection needs permission for this operation.");
        return actor;
    }
}
