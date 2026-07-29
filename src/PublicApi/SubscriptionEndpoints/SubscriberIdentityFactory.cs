using System;
using System.Globalization;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Builds a <see cref="SubscriberIdentity"/> from the authenticated caller's JWT. The caller's
/// identity comes entirely from the token (the name claim), never from the request body, so a
/// client cannot subscribe or read on behalf of another user.
/// </summary>
internal static class SubscriberIdentityFactory
{
    public static SubscriberIdentity? FromPrincipal(ClaimsPrincipal? principal)
    {
        var userName = principal?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        // eShopOnWeb user names are email addresses. Use the name claim as the stable reference so
        // the same user always maps to the same Maxio customer, even across process restarts.
        var email = userName.Contains('@', StringComparison.Ordinal)
            ? userName
            : $"{userName}@users.eshoponweb.local";

        var (firstName, lastName) = DeriveName(email);

        return new SubscriberIdentity(reference: userName, email: email, firstName: firstName, lastName: lastName);
    }

    private static (string FirstName, string LastName) DeriveName(string email)
    {
        var local = email.Split('@', 2)[0];
        var parts = local.Split(new[] { '.', '_', '-' }, 2, StringSplitOptions.RemoveEmptyEntries);

        var first = parts.Length > 0 ? Capitalize(parts[0]) : "eShopOnWeb";
        var last = parts.Length > 1 ? Capitalize(parts[1]) : "eShopOnWeb";
        return (first, last);
    }

    private static string Capitalize(string value) =>
        string.IsNullOrEmpty(value)
            ? value
            : char.ToUpper(value[0], CultureInfo.InvariantCulture) + value[1..];
}
