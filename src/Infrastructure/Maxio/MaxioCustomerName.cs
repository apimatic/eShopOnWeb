using System;
using System.Globalization;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Produces the <c>first_name</c>/<c>last_name</c> pair that <c>Create-Customer</c> requires.
/// eShopOnWeb accounts carry no name, so anything the caller supplies wins and the email local part
/// is only a fallback.
/// </summary>
internal static class MaxioCustomerName
{
    private const string FallbackLastName = "Customer";

    private static readonly char[] Separators = { '.', '_', '-', '+' };

    public static (string FirstName, string LastName) Resolve(Subscriber subscriber)
    {
        var first = subscriber.FirstName?.Trim();
        var last = subscriber.LastName?.Trim();

        if (!string.IsNullOrEmpty(first) && !string.IsNullOrEmpty(last))
        {
            return (first, last);
        }

        var (derivedFirst, derivedLast) = DeriveFromEmail(subscriber.Email);

        return (
            string.IsNullOrEmpty(first) ? derivedFirst : first,
            string.IsNullOrEmpty(last) ? derivedLast : last);
    }

    private static (string FirstName, string LastName) DeriveFromEmail(string email)
    {
        var at = email.IndexOf('@', StringComparison.Ordinal);
        var localPart = at > 0 ? email[..at] : email;

        var tokens = localPart
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Capitalize)
            .Where(token => token.Length > 0)
            .ToArray();

        return tokens.Length switch
        {
            0 => (email, FallbackLastName),
            1 => (tokens[0], FallbackLastName),
            _ => (tokens[0], string.Join(' ', tokens.Skip(1)))
        };
    }

    private static string Capitalize(string token) =>
        token.Length == 0 ? token : char.ToUpper(token[0], CultureInfo.InvariantCulture) + token[1..];
}
