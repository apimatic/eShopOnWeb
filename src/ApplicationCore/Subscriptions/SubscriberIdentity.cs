using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb shopper being billed, resolved from the caller's authenticated identity.
/// </summary>
public class SubscriberIdentity
{
    public SubscriberIdentity(string userName, string email, string firstName, string lastName)
    {
        UserName = Guard.Against.NullOrWhiteSpace(userName, nameof(userName));
        Email = Guard.Against.NullOrWhiteSpace(email, nameof(email));
        FirstName = Guard.Against.NullOrWhiteSpace(firstName, nameof(firstName));
        LastName = Guard.Against.NullOrWhiteSpace(lastName, nameof(lastName));
        BillingReference = BillingReferences.ForUser(userName);
    }

    /// <summary>ASP.NET Identity user name (the eShopOnWeb login).</summary>
    public string UserName { get; }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }

    /// <summary>
    /// Stable, deterministic key linking this shopper to their billing-provider customer record.
    /// Derived purely from <see cref="UserName"/>, so the link survives application restarts even
    /// when the local store is in-memory and re-seeds new primary keys.
    /// </summary>
    public string BillingReference { get; }

    /// <summary>
    /// Builds a display name suitable for the billing provider, which rejects blank names.
    /// </summary>
    public static (string FirstName, string LastName) DeriveName(string userName, string? email)
    {
        var source = email is { Length: > 0 } ? email : userName;
        var localPart = source.Split('@')[0].Split('+')[0];
        var parts = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);

        var first = parts.Length > 0 ? Titleize(parts[0]) : "eShopOnWeb";
        var last = parts.Length > 1 ? Titleize(string.Join(" ", parts, 1, parts.Length - 1)) : "Shopper";

        return (first, last);
    }

    private static string Titleize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value.Substring(1);
}
