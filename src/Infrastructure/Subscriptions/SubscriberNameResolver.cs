using System;
using System.Globalization;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions;

/// <summary>
/// Produces the first/last name pair Maxio requires when creating a customer.
/// </summary>
/// <remarks>
/// eShopOnWeb identities carry no personal name, so when the caller does not supply one the name is
/// derived from the email local part. That keeps the Maxio customer recognisable in the merchant UI
/// without inventing data the store does not hold.
/// </remarks>
internal static class SubscriberNameResolver
{
    private static readonly char[] LocalPartSeparators = { '.', '_', '-', '+' };

    /// <summary>Name used when the email yields a single token and nothing better is known.</summary>
    private const string FallbackLastName = "eShopOnWeb";

    public static (string FirstName, string LastName) Resolve(SubscriberIdentity subscriber)
    {
        var firstName = Clean(subscriber.FirstName);
        var lastName = Clean(subscriber.LastName);

        if (firstName is not null && lastName is not null)
        {
            return (firstName, lastName);
        }

        var identifier = Clean(subscriber.Email) ?? Clean(subscriber.UserKey) ?? FallbackLastName;
        var localPart = identifier.Split('@')[0];

        var tokens = localPart
            .Split(LocalPartSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Capitalize)
            .Where(t => t.Length > 0)
            .ToArray();

        var derivedFirst = tokens.Length > 0 ? tokens[0] : FallbackLastName;
        var derivedLast = tokens.Length > 1 ? string.Join(' ', tokens.Skip(1)) : FallbackLastName;

        return (firstName ?? derivedFirst, lastName ?? derivedLast);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Capitalize(string token) =>
        token.Length == 0
            ? token
            : char.ToUpper(token[0], CultureInfo.InvariantCulture) + token[1..];
}
