using System;
using System.Globalization;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Supplies the first and last name Advanced Billing requires on every customer.
/// </summary>
/// <remarks>
/// eShopOnWeb's Identity user carries an email and nothing else, but Advanced Billing rejects a customer
/// with a blank <c>first_name</c> or <c>last_name</c>. Callers may pass real names on the subscribe
/// request; when they do not, these are derived from the email rather than invented, so what lands in the
/// billing system is always traceable to something the shopper actually gave us.
/// </remarks>
internal static class MaxioCustomerNames
{
    /// <summary>Stands in for a family name when the email offers no second token to use.</summary>
    private const string LastNameFallback = "eShopOnWeb";

    private static readonly char[] LocalPartSeparators = { '.', '_', '-', '+' };

    public static (string FirstName, string LastName) Resolve(Subscriber subscriber)
    {
        var firstName = Clean(subscriber.FirstName);
        var lastName = Clean(subscriber.LastName);

        if (firstName is not null && lastName is not null)
        {
            return (firstName, lastName);
        }

        var (derivedFirst, derivedLast) = DeriveFromEmail(subscriber.Email);
        return (firstName ?? derivedFirst, lastName ?? derivedLast);
    }

    private static (string FirstName, string LastName) DeriveFromEmail(string email)
    {
        var localPart = email.Split('@')[0];

        var tokens = localPart
            .Split(LocalPartSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(TitleCase)
            .Where(t => t.Length > 0)
            .ToArray();

        return tokens.Length switch
        {
            0 => (LastNameFallback, LastNameFallback),
            1 => (tokens[0], LastNameFallback),
            _ => (tokens[0], string.Join(" ", tokens.Skip(1))),
        };
    }

    private static string TitleCase(string value) =>
        value.Length == 0
            ? value
            : char.ToUpper(value[0], CultureInfo.InvariantCulture) + value[1..];

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
