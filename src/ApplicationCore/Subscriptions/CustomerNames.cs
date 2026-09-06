using System;
using System.Globalization;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// eShopOnWeb identities carry no given/family name, but the billing system requires both on a
/// customer record. We derive a readable pair from the email local part and let callers override it.
/// </summary>
public static class CustomerNames
{
    /// <summary>Used when the email local part yields a single token, e.g. "demouser@microsoft.com".</summary>
    public const string FallbackLastName = "Subscriber";

    private static readonly char[] Separators = { '.', '_', '-', '+' };

    public static (string FirstName, string LastName) Resolve(string email, string? firstName = null, string? lastName = null)
    {
        var haveFirst = !string.IsNullOrWhiteSpace(firstName);
        var haveLast = !string.IsNullOrWhiteSpace(lastName);

        if (haveFirst && haveLast)
        {
            return (firstName!.Trim(), lastName!.Trim());
        }

        var localPart = (email ?? string.Empty).Split('@')[0];
        var tokens = localPart
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries)
            .Select(Capitalize)
            .Where(token => token.Length > 0)
            .ToArray();

        var derivedFirst = tokens.Length > 0 ? tokens[0] : FallbackLastName;
        var derivedLast = tokens.Length > 1 ? string.Join(" ", tokens.Skip(1)) : FallbackLastName;

        return (haveFirst ? firstName!.Trim() : derivedFirst, haveLast ? lastName!.Trim() : derivedLast);
    }

    private static string Capitalize(string token)
    {
        if (token.Length == 0)
        {
            return token;
        }

        return char.ToUpper(token[0], CultureInfo.InvariantCulture) + token.Substring(1);
    }
}
