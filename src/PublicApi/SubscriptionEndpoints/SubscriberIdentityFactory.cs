using System;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Projects the authenticated caller's JWT onto the identity the billing system needs.
/// <para>
/// The caller's identity comes from the token and nothing else — no request field names the shopper being
/// billed, so a caller can never subscribe somebody else.
/// </para>
/// </summary>
internal static class SubscriberIdentityFactory
{
    private static readonly char[] NameSeparators = { '.', '_', '-', '+' };

    /// <summary>Fallback surname: eShopOnWeb identities carry no name fields, and Maxio requires one.</summary>
    private const string FallbackLastName = "eShopOnWeb";

    /// <summary>
    /// Returns the caller's billing identity, or <c>null</c> when the token carries no usable identity.
    /// </summary>
    public static SubscriberIdentity? FromPrincipal(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var userName = principal.FindFirstValue(ClaimTypes.Name)
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.Identity.Name;

        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        userName = userName.Trim();

        // eShopOnWeb user names are e-mail addresses and are unique per account. They are used as the
        // stable key rather than the Identity primary key because the primary key does not survive a
        // restart when the application runs on the in-memory provider, whereas the account name does.
        var reference = userName.ToLowerInvariant();

        var email = principal.FindFirstValue(ClaimTypes.Email) ?? userName;
        var (firstName, lastName) = SplitName(email);

        return SubscriberIdentity.Create(reference, email, firstName, lastName);
    }

    /// <summary>
    /// Derives a first/last name from the account's e-mail address. eShopOnWeb stores no name fields, and
    /// the billing system requires both; a deployment that captures real names should pass those instead.
    /// </summary>
    private static (string FirstName, string LastName) SplitName(string email)
    {
        var localPart = email.Split('@')[0];
        var tokens = localPart
            .Split(NameSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length > 0)
            .ToArray();

        if (tokens.Length == 0)
        {
            return (email, FallbackLastName);
        }

        var firstName = Capitalize(tokens[0]);
        var lastName = tokens.Length > 1
            ? string.Join(" ", tokens.Skip(1).Select(Capitalize))
            : FallbackLastName;

        return (firstName, lastName);
    }

    private static string Capitalize(string value) =>
        value.Length <= 1
            ? value.ToUpper(CultureInfo.InvariantCulture)
            : char.ToUpper(value[0], CultureInfo.InvariantCulture) + value[1..];
}
