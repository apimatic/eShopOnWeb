using System;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class ShopperIdentityFactory
{
    public static ShopperIdentity FromClaims(ClaimsPrincipal user)
    {
        var username = user.FindFirst(ClaimTypes.Name)?.Value ?? user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException("The authenticated token carries no name claim.");
        }

        // eShopOnWeb usernames are email addresses; the token carries no name claims,
        // so the provider-side customer record derives its display names from the username.
        var email = user.FindFirst(ClaimTypes.Email)?.Value ?? username;
        var localPart = username.Split('@')[0];
        var firstName = user.FindFirst(ClaimTypes.GivenName)?.Value ?? localPart;
        var lastName = user.FindFirst(ClaimTypes.Surname)?.Value ?? "Shopper";

        return new ShopperIdentity(username, email, firstName, lastName);
    }
}
