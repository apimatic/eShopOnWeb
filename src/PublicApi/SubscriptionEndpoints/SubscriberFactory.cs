using System;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Builds a <see cref="Subscriber"/> from the JWT-authenticated caller. The identity
/// always comes from the token — never from the request body — so a caller can only
/// ever act on their own billing account. The user's name (email) is used as the
/// stable Maxio customer <c>reference</c>, which keeps customer creation idempotent
/// across requests and restarts.
/// </summary>
internal static class SubscriberFactory
{
    public static Subscriber? FromPrincipal(ClaimsPrincipal? principal)
    {
        var identifier = principal?.Identity?.Name
                         ?? principal?.FindFirstValue(ClaimTypes.Name)
                         ?? principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? principal?.FindFirstValue(ClaimTypes.Email);

        if (string.IsNullOrWhiteSpace(identifier))
        {
            return null;
        }

        var email = principal?.FindFirstValue(ClaimTypes.Email) is { Length: > 0 } claimEmail
            ? claimEmail
            : identifier;

        var (firstName, lastName) = SplitName(email);

        return new Subscriber
        {
            Reference = identifier,
            Email = email,
            FirstName = firstName,
            LastName = lastName
        };
    }

    // Derives presentable name parts from an email local-part, since eShop's demo
    // identity model stores no separate first/last name. Maxio requires both.
    private static (string FirstName, string LastName) SplitName(string email)
    {
        var local = email.Contains('@') ? email[..email.IndexOf('@')] : email;
        var parts = local.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);

        var firstName = parts.Length > 0 ? Capitalize(parts[0]) : "eShop";
        var lastName = parts.Length > 1 ? Capitalize(parts[^1]) : "eShopOnWeb";
        return (firstName, lastName);
    }

    private static string Capitalize(string value)
        => value.Length == 0 ? value : char.ToUpper(value[0], CultureInfo.InvariantCulture) + value[1..];
}
