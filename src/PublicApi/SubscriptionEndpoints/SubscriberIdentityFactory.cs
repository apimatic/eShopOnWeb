using System;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Builds a <see cref="SubscriberIdentity"/> from the authenticated caller's claims. Identity is
/// always server-derived from the JWT (never request input), and the username (an email in eShopOnWeb)
/// is used as the stable idempotency reference.
/// </summary>
internal static class SubscriberIdentityFactory
{
    private static readonly char[] NameSeparators = { '.', '_', '+', '-' };

    public static SubscriberIdentity? FromPrincipal(ClaimsPrincipal principal)
    {
        string? username = principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        string email = principal.FindFirstValue(ClaimTypes.Email) ?? username;
        (string firstName, string lastName) = DeriveName(email);

        return new SubscriberIdentity(reference: username, email: email, firstName: firstName, lastName: lastName);
    }

    private static (string firstName, string lastName) DeriveName(string email)
    {
        int at = email.IndexOf('@');
        string local = at > 0 ? email[..at] : email;
        string[] parts = local.Split(NameSeparators, StringSplitOptions.RemoveEmptyEntries);

        string firstName = parts.Length > 0 ? Capitalize(parts[0]) : "eShop";
        string lastName = parts.Length > 1 ? Capitalize(parts[^1]) : "Customer";
        return (firstName, lastName);
    }

    private static string Capitalize(string value) =>
        value.Length <= 1 ? value.ToUpperInvariant() : char.ToUpperInvariant(value[0]) + value[1..];
}
