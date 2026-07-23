using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves which user a subscription request acts on, enforcing that only administrators may act
/// on somebody else's subscription.
/// </summary>
internal static class SubscriptionCaller
{
    /// <summary>
    /// Returns the user reference the caller is allowed to act on.
    /// </summary>
    /// <param name="user">The authenticated principal.</param>
    /// <param name="requestedReference">
    /// The user the caller asked to act on, if any. Honoured only for administrators.
    /// </param>
    /// <returns>
    /// The caller's own reference, the requested reference when the caller is an administrator, or
    /// null when a non-administrator asked to act on somebody else.
    /// </returns>
    public static string? ResolveUserReference(ClaimsPrincipal user, string? requestedReference)
    {
        var own = user.Identity?.Name
                  ?? user.FindFirstValue(ClaimTypes.Name)
                  ?? user.FindFirstValue(ClaimTypes.Email);

        if (string.IsNullOrWhiteSpace(requestedReference))
        {
            return string.IsNullOrWhiteSpace(own) ? null : own;
        }

        if (IsAdministrator(user))
        {
            return requestedReference;
        }

        // A non-administrator may only ever act on themselves.
        return !string.IsNullOrWhiteSpace(own) &&
               string.Equals(own, requestedReference, System.StringComparison.OrdinalIgnoreCase)
            ? own
            : null;
    }

    public static bool IsAdministrator(ClaimsPrincipal user)
        => user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);

    /// <summary>
    /// Denies the request with a genuine 403.
    /// </summary>
    /// <remarks>
    /// Deliberately not <c>Results.Forbid()</c>: this host registers ASP.NET Core Identity, whose
    /// cookie handler is the default forbid scheme and would answer an API caller with a 302
    /// redirect to a login page instead of a status a JWT client can act on.
    /// </remarks>
    public static IResult Forbidden() =>
        Results.Problem(
            title: "Forbidden",
            detail: "You may only act on your own subscription.",
            statusCode: StatusCodes.Status403Forbidden);

    /// <summary>
    /// True when the caller may act on the given subscription: administrators may act on any,
    /// everybody else only on their own.
    /// </summary>
    public static bool CanActOn(ClaimsPrincipal user, string? subscriptionOwnerReference)
    {
        if (IsAdministrator(user))
        {
            return true;
        }

        var own = user.Identity?.Name
                  ?? user.FindFirstValue(ClaimTypes.Name)
                  ?? user.FindFirstValue(ClaimTypes.Email);

        return !string.IsNullOrWhiteSpace(own)
               && !string.IsNullOrWhiteSpace(subscriptionOwnerReference)
               && string.Equals(own, subscriptionOwnerReference, System.StringComparison.OrdinalIgnoreCase);
    }
}
