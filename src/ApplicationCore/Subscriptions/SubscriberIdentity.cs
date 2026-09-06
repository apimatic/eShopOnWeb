using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb-side identity of the shopper a subscription belongs to. This is the only thing the
/// billing system is told about "who" the subscriber is; the billing adapter projects it onto a
/// stable customer reference.
/// </summary>
public class SubscriberIdentity
{
    public SubscriberIdentity(string userName, string email, string? firstName = null, string? lastName = null)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));
        Guard.Against.NullOrWhiteSpace(email, nameof(email));

        UserName = userName.Trim();
        Email = email.Trim();
        FirstName = string.IsNullOrWhiteSpace(firstName) ? DeriveFirstName(Email) : firstName!.Trim();
        LastName = string.IsNullOrWhiteSpace(lastName) ? DeriveLastName(Email) : lastName!.Trim();
    }

    /// <summary>The eShopOnWeb login name, carried on the JWT as the name claim.</summary>
    public string UserName { get; }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }

    /// <summary>
    /// The value the billing-system customer reference is derived from. eShopOnWeb identity keys are
    /// regenerated whenever the in-memory identity store is rebuilt, so the login name — stable and
    /// unique by construction — is what anchors a shopper to their billing customer across restarts.
    /// </summary>
    public string StableKey => UserName.ToLowerInvariant();

    private static readonly char[] NameSeparators = { '.', '_', '-', '+' };

    private static string DeriveFirstName(string email)
    {
        var parts = LocalPart(email).Split(NameSeparators, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? Capitalize(parts[0]) : LocalPart(email);
    }

    private static string DeriveLastName(string email)
    {
        var parts = LocalPart(email).Split(NameSeparators, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? Capitalize(parts[parts.Length - 1]) : "eShopOnWeb";
    }

    private static string LocalPart(string email)
    {
        var at = email.IndexOf('@');
        return at > 0 ? email.Substring(0, at) : email;
    }

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value.Substring(1);
}
