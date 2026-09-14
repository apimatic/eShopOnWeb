using System;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the billing identity of a subscriber strictly from the authenticated JWT principal, so a
/// caller can only ever act on their own account. The eShopOnWeb user name (which is their email) is
/// used as the stable Maxio customer <c>reference</c>.
/// </summary>
internal static class SubscriberIdentity
{
    public static bool TryResolve(ClaimsPrincipal? principal, out string userReference, out string email, out string firstName, out string lastName)
    {
        userReference = string.Empty;
        email = string.Empty;
        firstName = string.Empty;
        lastName = string.Empty;

        var name = principal?.Identity?.Name
                   ?? principal?.FindFirstValue(ClaimTypes.Name)
                   ?? principal?.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        userReference = name;

        // In eShopOnWeb the user name is the email address; fall back to a synthetic address if not.
        email = name.Contains('@', StringComparison.Ordinal)
            ? name
            : $"{name}@users.eshoponweb.local";

        var localPart = email.Split('@', 2)[0];
        firstName = string.IsNullOrWhiteSpace(localPart) ? name : localPart;
        lastName = "eShopOnWeb";

        return true;
    }

    public static SubscribeRequest BuildSubscribeRequest(ClaimsPrincipal principal, string planHandle)
    {
        TryResolve(principal, out var reference, out var email, out var firstName, out var lastName);
        return new SubscribeRequest
        {
            UserReference = reference,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            PlanHandle = planHandle
        };
    }
}
