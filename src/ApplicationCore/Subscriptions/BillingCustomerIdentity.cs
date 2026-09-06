using System;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb view of a shopper as the billing system needs to see them.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Reference"/> is the stable, app-owned key that links an eShopOnWeb user to exactly one
/// customer record in the billing system. It is derived from the user name (which is the e-mail address in
/// eShopOnWeb, and is unique and never reused) rather than from the Identity primary key on purpose: the
/// primary key is regenerated whenever the app runs against the in-memory database, which would strand the
/// previous run's billing customer and create a fresh one on every restart.
/// </para>
/// </remarks>
public sealed record BillingCustomerIdentity
{
    /// <summary>Namespaces the reference so it cannot collide with records created by another system.</summary>
    public const string ReferencePrefix = "eshoponweb-";

    private BillingCustomerIdentity(string reference, string email, string firstName, string lastName)
    {
        Reference = reference;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }

    /// <summary>Stable, unique, never-reused external key for this shopper.</summary>
    public string Reference { get; }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }

    /// <summary>
    /// Builds the billing identity for an authenticated eShopOnWeb user. Deterministic: the same user name
    /// always produces the same <see cref="Reference"/>, which is what makes "ensure the customer exists"
    /// idempotent.
    /// </summary>
    public static BillingCustomerIdentity ForUser(string userName, string? email = null)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));

        var normalized = userName.Trim().ToLowerInvariant();
        var effectiveEmail = string.IsNullOrWhiteSpace(email) ? normalized : email!.Trim();
        var (firstName, lastName) = DeriveName(effectiveEmail);

        return new BillingCustomerIdentity(ReferencePrefix + normalized, effectiveEmail, firstName, lastName);
    }

    /// <summary>
    /// eShopOnWeb's identity records carry no given/family name, but the billing provider requires both.
    /// Derive something readable from the e-mail local part and fall back to fixed placeholders, so an
    /// operator looking at the billing console sees where the record came from.
    /// </summary>
    private static (string FirstName, string LastName) DeriveName(string email)
    {
        var localPart = email.Split('@')[0];
        var parts = localPart
            .Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(part => part.Length > 0)
            .ToArray();

        var first = parts.Length > 0 ? Titleize(parts[0]) : "eShopOnWeb";
        var last = parts.Length > 1 ? Titleize(parts[parts.Length - 1]) : "Customer";

        return (first, last);
    }

    private static string Titleize(string value) =>
        char.ToUpperInvariant(value[0]) + value.Substring(1);
}
