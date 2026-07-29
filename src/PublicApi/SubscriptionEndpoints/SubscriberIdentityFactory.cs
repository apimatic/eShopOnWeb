using System.Net;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Builds a <see cref="SubscriberIdentity"/> from the authenticated caller's claims. The identity is
/// taken from the JWT (never from request input), so a subscription is always tied to the token holder.
/// The eShopOnWeb token carries the user's login (an email) as the name claim; that login is used as the
/// stable, unique Maxio customer <c>reference</c>, which is what makes "ensure a customer exists" idempotent.
/// </summary>
public static class SubscriberIdentityFactory
{
    public static SubscriberIdentity FromPrincipal(ClaimsPrincipal user)
    {
        var login = user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(login))
        {
            throw new SubscriptionBillingException("The authenticated user has no name claim.", HttpStatusCode.Unauthorized);
        }

        var email = user.FindFirstValue(ClaimTypes.Email) ?? login;
        var (firstName, lastName) = DeriveName(email);

        return new SubscriberIdentity(Reference: login, Email: email, FirstName: firstName, LastName: lastName);
    }

    private static (string FirstName, string LastName) DeriveName(string email)
    {
        // Maxio requires non-empty first/last names; derive a reasonable display name from the login.
        var localPart = email.Contains('@') ? email[..email.IndexOf('@')] : email;
        var firstName = string.IsNullOrWhiteSpace(localPart) ? "eShop" : localPart;
        return (firstName, "eShopOnWeb Customer");
    }
}
