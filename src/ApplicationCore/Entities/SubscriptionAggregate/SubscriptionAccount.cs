using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Maps an eShopOnWeb application user to their billing account (customer) in Maxio
/// Advanced Billing. Maxio Advanced Billing is the system of record for the actual
/// subscriptions; this aggregate only persists the stable cross-reference used to keep
/// Maxio lookups idempotent and cheap.
/// </summary>
public class SubscriptionAccount : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SubscriptionAccount()
    {
    }

    public SubscriptionAccount(
        string applicationUserId,
        string applicationUserEmail,
        long maxioCustomerId,
        string maxioCustomerReference)
    {
        Guard.Against.NullOrEmpty(applicationUserId, nameof(applicationUserId));
        Guard.Against.NullOrEmpty(applicationUserEmail, nameof(applicationUserEmail));
        Guard.Against.OutOfRange(maxioCustomerId, nameof(maxioCustomerId), 1, long.MaxValue);
        Guard.Against.NullOrEmpty(maxioCustomerReference, nameof(maxioCustomerReference));

        ApplicationUserId = applicationUserId;
        ApplicationUserEmail = applicationUserEmail;
        MaxioCustomerId = maxioCustomerId;
        MaxioCustomerReference = maxioCustomerReference;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = null;
    }

    /// <summary>The eShopOnWeb identity user id (ApplicationUser.Id).</summary>
    public string ApplicationUserId { get; private set; }

    /// <summary>The eShopOnWeb identity user e-mail, kept for diagnostics.</summary>
    public string ApplicationUserEmail { get; private set; }

    /// <summary>The Maxio Advanced Billing customer id.</summary>
    public long MaxioCustomerId { get; private set; }

    /// <summary>
    /// The Maxio customer reference value. This is always the eShopOnWeb user id so it is
    /// stable and unique within Maxio.
    /// </summary>
    public string MaxioCustomerReference { get; private set; }

    /// <summary>UTC timestamp of the first time the mapping was persisted.</summary>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>UTC timestamp of the last time the mapping was refreshed.</summary>
    public DateTime? UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Refreshes the locally cached e-mail / customer reference used by the mapping.
    /// </summary>
    public void Update(string applicationUserEmail)
    {
        Guard.Against.NullOrEmpty(applicationUserEmail, nameof(applicationUserEmail));
        ApplicationUserEmail = applicationUserEmail;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
