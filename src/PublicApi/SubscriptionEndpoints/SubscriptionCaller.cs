using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Derives the billing identity of the authenticated caller from their JWT claims. The user's
/// name claim (their eShopOnWeb username, an email) is the stable Maxio customer reference.
/// </summary>
internal static class SubscriptionCaller
{
    public static string GetReference(ClaimsPrincipal user)
    {
        var name = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
            throw new SubscriptionBillingException("The authenticated token does not carry a user identity.", 401);
        return name;
    }

    public static SubscribeRequest ToSubscribeRequest(ClaimsPrincipal user, string planHandle)
    {
        var reference = GetReference(user);
        var email = reference.Contains('@') ? reference : $"{reference}@users.eshoponweb.local";

        var localPart = email.Split('@')[0];
        var nameParts = localPart.Split('.', 2);
        var firstName = Capitalize(nameParts[0]);
        var lastName = nameParts.Length > 1 ? Capitalize(nameParts[1]) : "(eShopOnWeb)";

        return new SubscribeRequest(reference, email, firstName, lastName, planHandle);
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "eShop";
        return char.ToUpperInvariant(value[0]) + value[1..];
    }
}
