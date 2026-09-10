using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Builds a <see cref="SubscriberIdentity"/> from the authenticated caller. The caller's identity comes
/// solely from the JWT: the name claim (the eShop login, which is the user's email) is used as the
/// stable billing-customer reference, so a user maps to exactly one billing customer.
/// </summary>
internal static class SubscriberIdentityFactory
{
    public static SubscriberIdentity? FromPrincipal(ClaimsPrincipal user, string? firstName = null, string? lastName = null)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return new SubscriberIdentity
        {
            Reference = name,
            Email = name,
            FirstName = firstName,
            LastName = lastName,
        };
    }
}
