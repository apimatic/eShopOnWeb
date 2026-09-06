using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Builds the subscriber a request acts on, strictly from the bearer token. Nothing about the
/// shopper is ever read from the request body, so a caller cannot subscribe - or list subscriptions -
/// on someone else's behalf.
/// </summary>
public static class SubscriberIdentityFactory
{
    public static bool TryCreate(
        ClaimsPrincipal? principal,
        [NotNullWhen(true)] out SubscriberIdentity? subscriber,
        [NotNullWhen(false)] out string? error)
    {
        subscriber = null;
        error = null;

        var userName = principal?.FindFirstValue(ClaimTypes.Name)
                       ?? principal?.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userName))
        {
            error = "The access token does not identify a user.";
            return false;
        }

        // eShopOnWeb registers users with their e-mail address as the user name, so the name claim is
        // the fallback when the token predates the e-mail claim.
        var email = principal?.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email))
        {
            email = userName;
        }

        if (!email.Contains('@'))
        {
            error = "Billing requires an e-mail address for the signed-in user, and the access token does not carry one.";
            return false;
        }

        subscriber = new SubscriberIdentity(
            userName,
            email,
            principal?.FindFirstValue(ClaimTypes.GivenName),
            principal?.FindFirstValue(ClaimTypes.Surname));

        return true;
    }
}
