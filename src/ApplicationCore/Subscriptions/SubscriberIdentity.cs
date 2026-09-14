using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Identifies the eShopOnWeb shopper that a billing operation is performed on behalf of.
/// The values are always derived from the authenticated principal, never from request input.
/// </summary>
public class SubscriberIdentity
{
    /// <summary>
    /// Namespace applied to <see cref="BillingReference"/> so that a single billing site can safely
    /// host customers coming from more than one application.
    /// </summary>
    public const string ReferencePrefix = "eshoponweb:";

    public SubscriberIdentity(string userName, string email, string? firstName = null, string? lastName = null)
    {
        UserName = Guard.Against.NullOrWhiteSpace(userName, nameof(userName));
        Email = Guard.Against.NullOrWhiteSpace(email, nameof(email));
        FirstName = string.IsNullOrWhiteSpace(firstName) ? DeriveFirstName(email) : firstName!.Trim();
        LastName = string.IsNullOrWhiteSpace(lastName) ? DeriveLastName(email) : lastName!.Trim();
    }

    /// <summary>The eShopOnWeb user name (taken from the JWT <c>name</c> claim).</summary>
    public string UserName { get; }

    public string Email { get; }

    public string FirstName { get; }

    public string LastName { get; }

    /// <summary>
    /// Stable, deterministic key used as the billing provider's customer <c>reference</c>. Deriving it from
    /// the e-mail address (rather than the ASP.NET Identity row id) keeps the mapping intact across restarts,
    /// which is what makes "ensure the customer exists" idempotent without any local persistence.
    /// </summary>
    public string BillingReference => ReferencePrefix + Email.Trim().ToLowerInvariant();

    private static string DeriveFirstName(string email)
    {
        var localPart = LocalPart(email);
        var parts = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? Titleize(parts[0]) : "eShopOnWeb";
    }

    private static string DeriveLastName(string email)
    {
        var localPart = LocalPart(email);
        var parts = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? Titleize(parts[1]) : "Customer";
    }

    private static string LocalPart(string email)
    {
        var at = email.IndexOf('@');
        return at > 0 ? email.Substring(0, at) : email;
    }

    private static string Titleize(string value) =>
        value.Length == 1 ? value.ToUpperInvariant() : char.ToUpperInvariant(value[0]) + value.Substring(1);
}
