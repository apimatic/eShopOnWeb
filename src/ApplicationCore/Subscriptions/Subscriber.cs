using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb account being enrolled, together with the details the billing provider
/// needs in order to open a customer record for it.
/// </summary>
public class Subscriber
{
    public Subscriber(string userName, string email, string? firstName = null, string? lastName = null)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("A subscriber must have a user name.", nameof(userName));
        }

        UserName = userName.Trim();
        Email = string.IsNullOrWhiteSpace(email) ? UserName : email.Trim();

        var (derivedFirst, derivedLast) = DeriveName(UserName);
        FirstName = Coalesce(firstName, derivedFirst);
        LastName = Coalesce(lastName, derivedLast);
    }

    public string UserName { get; }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }

    /// <summary>
    /// The value stored on the billing provider's customer record so that an eShopOnWeb account
    /// can always be mapped back to exactly one provider customer.
    /// </summary>
    /// <remarks>
    /// Keyed on the account's user name rather than its ASP.NET Identity primary key on purpose:
    /// the user name is the account's canonical, immutable login, whereas the Identity key is
    /// regenerated whenever the store is re-seeded (which the in-memory provider does on every
    /// restart). Keying on the login keeps the eShopOnWeb account bound to the same billing
    /// customer across restarts, and keeps the billing provider — not a local table — the system
    /// of record for the mapping.
    /// </remarks>
    public string CustomerReference => $"eshoponweb:{UserName.ToLowerInvariant()}";

    private static string Coalesce(string? supplied, string fallback) =>
        string.IsNullOrWhiteSpace(supplied) ? fallback : supplied.Trim();

    /// <summary>
    /// Billing providers generally require a non-blank given/family name, but an eShopOnWeb
    /// account only carries a login. Derive a stable placeholder from the login's local part
    /// so an unattended signup never fails validation.
    /// </summary>
    private static (string FirstName, string LastName) DeriveName(string userName)
    {
        var localPart = userName.Split('@')[0];
        var tokens = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length == 0)
        {
            return ("eShopOnWeb", "Customer");
        }

        var first = Capitalize(tokens[0]);
        var last = tokens.Length > 1
            ? string.Join(" ", Array.ConvertAll(tokens[1..], Capitalize))
            : "Customer";

        return (first, last);
    }

    private static string Capitalize(string value) =>
        value.Length <= 1 ? value.ToUpperInvariant() : char.ToUpperInvariant(value[0]) + value[1..];
}
