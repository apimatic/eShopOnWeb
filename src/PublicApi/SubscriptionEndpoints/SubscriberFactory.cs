using System;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Builds a <see cref="Subscriber"/> from the authenticated caller's JWT. The caller's
/// identity comes entirely from the token; the resolved user name is used as the stable
/// billing customer reference so the same user always maps to the same customer.
/// </summary>
internal static class SubscriberFactory
{
    public static Subscriber? FromPrincipal(ClaimsPrincipal principal)
    {
        var identity = ResolveIdentity(principal);
        if (string.IsNullOrWhiteSpace(identity))
        {
            return null;
        }

        var email = identity.Contains('@', StringComparison.Ordinal)
            ? identity
            : $"{identity}@users.eshoponweb.local";

        var localPart = email.Split('@', 2)[0];
        var firstName = string.IsNullOrWhiteSpace(localPart) ? identity : localPart;

        return new Subscriber(
            reference: identity,
            email: email,
            firstName: firstName,
            lastName: "eShopOnWeb");
    }

    private static string? ResolveIdentity(ClaimsPrincipal principal)
    {
        if (!string.IsNullOrWhiteSpace(principal.Identity?.Name))
        {
            return principal.Identity!.Name;
        }

        // Fall back across the claim types the token may carry.
        foreach (var claimType in new[] { ClaimTypes.Name, ClaimTypes.Email, "unique_name", "email", "sub", ClaimTypes.NameIdentifier })
        {
            var value = principal.FindFirstValue(claimType);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
