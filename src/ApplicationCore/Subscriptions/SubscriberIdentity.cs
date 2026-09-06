using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb-side identity a subscription is created for, plus the billing-provider customer
/// reference derived from it.
/// </summary>
public class SubscriberIdentity
{
    private static readonly char[] NameSeparators = { '.', '_', '-', '+' };

    public SubscriberIdentity(string userName, string email, string customerReference, string? firstName = null, string? lastName = null)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("A user name is required.", nameof(userName));
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("An email address is required.", nameof(email));
        }

        UserName = userName;
        Email = email;
        CustomerReference = customerReference;

        var (derivedFirst, derivedLast) = DeriveName(email);
        FirstName = string.IsNullOrWhiteSpace(firstName) ? derivedFirst : firstName.Trim();
        LastName = string.IsNullOrWhiteSpace(lastName) ? derivedLast : lastName.Trim();
    }

    public string UserName { get; }

    public string Email { get; }

    /// <summary>The value stored as <c>reference</c> on the billing-provider customer record.</summary>
    public string CustomerReference { get; }

    public string FirstName { get; }

    public string LastName { get; }

    /// <summary>
    /// Builds the customer reference for a shopper. The email address is used rather than the
    /// ASP.NET Identity primary key because it is the shopper identity that stays stable across
    /// deployments and across the in-memory Identity store used for local runs, and because the
    /// reference has to be reproducible for the "ensure a customer exists" step to be idempotent.
    /// </summary>
    public static string BuildCustomerReference(string prefix, string email)
    {
        var normalized = (email ?? string.Empty).Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(prefix) ? normalized : $"{prefix}-{normalized}";
    }

    /// <summary>
    /// Derives a first and last name from an email address. The billing provider requires both and
    /// eShopOnWeb identities carry neither, so callers that do know the shopper name should pass it
    /// explicitly; this is the deterministic fallback.
    /// </summary>
    private static (string FirstName, string LastName) DeriveName(string email)
    {
        var at = email.IndexOf('@');
        var localPart = at > 0 ? email[..at] : email;
        var domain = at >= 0 && at < email.Length - 1 ? email[(at + 1)..] : string.Empty;

        var tokens = localPart.Split(NameSeparators, StringSplitOptions.RemoveEmptyEntries);
        var first = tokens.Length > 0 ? Titleize(tokens[0]) : Titleize(localPart);

        string last;
        if (tokens.Length > 1)
        {
            last = string.Join(' ', tokens.Skip(1).Select(Titleize));
        }
        else
        {
            var domainLabel = domain.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            last = Titleize(domainLabel ?? string.Empty);
        }

        if (string.IsNullOrWhiteSpace(first))
        {
            first = "eShop";
        }

        if (string.IsNullOrWhiteSpace(last))
        {
            last = "Subscriber";
        }

        return (first, last);
    }

    private static string Titleize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        builder.Append(char.ToUpper(value[0], CultureInfo.InvariantCulture));
        if (value.Length > 1)
        {
            builder.Append(value[1..]);
        }

        return builder.ToString();
    }
}
