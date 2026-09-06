using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb-side identity of the shopper being billed. Assembled from the authenticated
/// principal — never from untrusted request content — and projected onto the billing system's
/// customer record.
/// </summary>
public class SubscriberIdentity
{
    public SubscriberIdentity(string userName, string email, string? firstName = null, string? lastName = null)
    {
        UserName = Guard.Against.NullOrWhiteSpace(userName, nameof(userName));
        Email = Guard.Against.NullOrWhiteSpace(email, nameof(email));
        FirstName = string.IsNullOrWhiteSpace(firstName) ? null : firstName.Trim();
        LastName = string.IsNullOrWhiteSpace(lastName) ? null : lastName.Trim();
    }

    /// <summary>The eShopOnWeb login name. Unique per user and stable across restarts, so it is the
    /// basis for the billing customer reference.</summary>
    public string UserName { get; }

    public string Email { get; }

    /// <summary>Optional given name supplied by the caller; derived from <see cref="Email"/> when absent.</summary>
    public string? FirstName { get; }

    /// <summary>Optional family name supplied by the caller; derived from <see cref="Email"/> when absent.</summary>
    public string? LastName { get; }
}
