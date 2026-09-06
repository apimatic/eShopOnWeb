using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Everything the billing provider needs in order to have a customer record for an eShopOnWeb user.
/// </summary>
public class SubscriberProfile
{
    public SubscriberProfile(string userKey, string email, string? firstName = null, string? lastName = null, string? organization = null)
    {
        Guard.Against.NullOrWhiteSpace(userKey, nameof(userKey));
        Guard.Against.NullOrWhiteSpace(email, nameof(email));

        UserKey = userKey;
        Email = email;
        FirstName = string.IsNullOrWhiteSpace(firstName) ? DeriveFirstName(email) : firstName!.Trim();
        LastName = string.IsNullOrWhiteSpace(lastName) ? DeriveLastName(email) : lastName!.Trim();
        Organization = string.IsNullOrWhiteSpace(organization) ? null : organization!.Trim();
    }

    /// <summary>
    /// Stable, lower-cased identity of the eShopOnWeb user (its user name, which is the sign-in email).
    /// This is what the provider-side customer reference is derived from, so the mapping survives a
    /// restart even when the identity store is in-memory and re-seeds new user ids.
    /// </summary>
    public string UserKey { get; }

    public string Email { get; }

    /// <summary>Maxio requires a first name on customer creation (<c>Create-Customer.yaml</c> marks it required).</summary>
    public string FirstName { get; }

    /// <summary>Maxio requires a last name on customer creation (<c>Create-Customer.yaml</c> marks it required).</summary>
    public string LastName { get; }

    public string? Organization { get; }

    /// <summary>
    /// eShopOnWeb only knows a shopper's email address, but Maxio requires a first and last name.
    /// Derive something sensible from the local part of the address; callers who know better can
    /// always supply the real names on the subscribe request.
    /// </summary>
    private static string DeriveFirstName(string email)
    {
        var parts = SplitLocalPart(email);
        return Titleize(parts.Length > 0 ? parts[0] : email);
    }

    private static string DeriveLastName(string email)
    {
        var parts = SplitLocalPart(email);
        return parts.Length > 1 ? Titleize(parts[1]) : "Customer";
    }

    private static string[] SplitLocalPart(string email)
    {
        var at = email.IndexOf('@');
        var localPart = at > 0 ? email.Substring(0, at) : email;
        return localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static string Titleize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Customer";
        return value.Length == 1
            ? value.ToUpperInvariant()
            : char.ToUpperInvariant(value[0]) + value.Substring(1);
    }
}
