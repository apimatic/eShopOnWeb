using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Builds the shopper identity used for billing from the bearer token. eShopOnWeb issues the
    /// user name as the name claim, and user names are email addresses; an email claim is preferred
    /// when the token carries one. Returns null when the token identifies no user.
    /// </summary>
    public static SubscriberIdentity? ToSubscriberIdentity(this ClaimsPrincipal? user,
        string? firstName = null, string? lastName = null, string? organization = null)
    {
        var userName = user?.Identity?.Name ?? user?.FindFirstValue(ClaimTypes.Name);

        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var email = user?.FindFirstValue(ClaimTypes.Email);

        return new SubscriberIdentity
        {
            UserName = userName,
            Email = string.IsNullOrWhiteSpace(email) ? userName : email,
            FirstName = firstName,
            LastName = lastName,
            Organization = organization
        };
    }
}
