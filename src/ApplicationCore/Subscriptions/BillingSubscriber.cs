using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Identifies the eShopOnWeb user that a Maxio customer/subscription is created for.
/// <para>
/// <see cref="UserId"/> is a stable identifier from our own application (the ASP.NET Identity
/// user id). It is used as the Maxio customer <c>reference</c>, which is the mechanism Maxio
/// provides for mapping an external user to a single customer record and is the basis for
/// idempotent customer provisioning.
/// </para>
/// </summary>
public sealed class BillingSubscriber
{
    public BillingSubscriber(string userId, string email, string? firstName = null, string? lastName = null)
    {
        UserId = Guard.Against.NullOrWhiteSpace(userId, nameof(userId));
        Email = Guard.Against.NullOrWhiteSpace(email, nameof(email));
        FirstName = firstName;
        LastName = lastName;
    }

    /// <summary>Stable application user id. Used verbatim as the Maxio customer reference.</summary>
    public string UserId { get; }

    public string Email { get; }

    public string? FirstName { get; }

    public string? LastName { get; }
}
