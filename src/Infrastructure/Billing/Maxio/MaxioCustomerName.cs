using System;
using System.Globalization;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Maxio requires a first and last name on every customer. eShopOnWeb's identity store only holds an
/// e-mail address, so when the caller does not supply names we derive presentable ones from the
/// address rather than sending blanks: <c>ann.lee@contoso.com</c> becomes "Ann Lee",
/// <c>demouser@microsoft.com</c> becomes "Demouser Microsoft".
/// </summary>
public static class MaxioCustomerName
{
    private static readonly char[] LocalPartSeparators = { '.', '_', '-', '+' };

    public static (string FirstName, string LastName, string? Organization) Derive(SubscriberIdentity subscriber)
    {
        var email = subscriber.Email;
        var at = email.IndexOf('@');
        var localPart = at > 0 ? email[..at] : email;
        var domain = at >= 0 && at < email.Length - 1 ? email[(at + 1)..] : string.Empty;

        var tokens = localPart
            .Split(LocalPartSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 0)
            .ToArray();

        var organization = subscriber.Organization ?? (domain.Length > 0 ? domain : null);

        var firstName = subscriber.FirstName
                        ?? (tokens.Length > 0 ? Titleize(tokens[0]) : Titleize(localPart));

        var lastName = subscriber.LastName
                       ?? (tokens.Length > 1
                           ? string.Join(' ', tokens.Skip(1).Select(Titleize))
                           : Titleize(DomainLabel(domain)));

        if (string.IsNullOrWhiteSpace(firstName))
        {
            firstName = subscriber.UserName;
        }

        if (string.IsNullOrWhiteSpace(lastName))
        {
            // Maxio rejects a blank last name; fall back to the app name so the record stays legible.
            lastName = "eShopOnWeb";
        }

        return (firstName, lastName, organization);
    }

    private static string DomainLabel(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return string.Empty;
        }

        var dot = domain.IndexOf('.');
        return dot > 0 ? domain[..dot] : domain;
    }

    private static string Titleize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return char.ToUpper(value[0], CultureInfo.InvariantCulture) + value[1..];
    }
}
