using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// A per-user claim on a billing-provider write. The row is inserted (keyed by the user) BEFORE the
/// provider is called, so a second concurrent request for the same user is refused by the store's
/// primary key instead of reaching the provider twice.
/// </summary>
public abstract class BillingClaim
{
    /// <summary>The eShopOnWeb user this claim belongs to (normalized user name). Primary key.</summary>
    public string UserKey { get; protected set; } = string.Empty;

    /// <summary>The reference sent to the billing provider; used to find the write again when its outcome is unknown.</summary>
    public string ProviderReference { get; protected set; } = string.Empty;

    public BillingClaimStatus Status { get; protected set; }

    /// <summary>While Pending, the claim is held until this instant; after it, another request may take it over.</summary>
    public DateTimeOffset LeaseExpiresAt { get; protected set; }

    public DateTimeOffset UpdatedAt { get; protected set; }

    /// <summary>Optimistic concurrency token, so only one request can take over an expired lease.</summary>
    public Guid Version { get; set; }

    protected void Start(string userKey, string providerReference, DateTimeOffset now, TimeSpan lease)
    {
        Guard.Against.NullOrWhiteSpace(userKey, nameof(userKey));
        Guard.Against.NullOrWhiteSpace(providerReference, nameof(providerReference));
        UserKey = userKey;
        ProviderReference = providerReference;
        Status = BillingClaimStatus.Pending;
        LeaseExpiresAt = now + lease;
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }

    public bool IsLeaseActive(DateTimeOffset now) => Status == BillingClaimStatus.Pending && LeaseExpiresAt > now;

    public void RenewLease(DateTimeOffset now, TimeSpan lease)
    {
        LeaseExpiresAt = now + lease;
        UpdatedAt = now;
    }

    protected void MarkCompleted(DateTimeOffset now)
    {
        Status = BillingClaimStatus.Completed;
        UpdatedAt = now;
    }
}

public enum BillingClaimStatus
{
    Pending = 0,
    Completed = 1
}
