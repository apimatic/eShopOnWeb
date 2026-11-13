using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// A claim on a write to the billing provider, keyed by the deterministic reference that is also sent to the
/// provider. The primary key makes the store refuse a second claim for the same reference, so a double-click
/// or two racing requests can never both reach the provider. <see cref="LeaseExpiresUtc"/> lets a claim whose
/// holder died be taken over; the taker re-reads the provider by reference before it writes anything.
/// </summary>
public abstract class BillingClaim
{
    protected BillingClaim(string reference, DateTime nowUtc, TimeSpan lease)
    {
        Reference = Guard.Against.NullOrWhiteSpace(reference, nameof(reference));
        Status = BillingClaimStatus.Pending;
        CreatedUtc = nowUtc;
        UpdatedUtc = nowUtc;
        LeaseExpiresUtc = nowUtc + lease;
    }

#pragma warning disable CS8618 // Required by Entity Framework
    protected BillingClaim() { }
#pragma warning restore CS8618

    /// <summary>The deterministic reference sent to the billing provider. Primary key.</summary>
    public string Reference { get; private set; }

    public BillingClaimStatus Status { get; private set; }

    /// <summary>The provider's id for the record, once known.</summary>
    public long? ProviderId { get; private set; }

    public DateTime CreatedUtc { get; private set; }

    public DateTime UpdatedUtc { get; private set; }

    /// <summary>Until this instant a <see cref="BillingClaimStatus.Pending"/> claim belongs to the request that took it.</summary>
    public DateTime LeaseExpiresUtc { get; private set; }

    /// <summary>Optimistic-concurrency token, bumped on every change so two takers can never both win.</summary>
    public int Version { get; private set; }

    public bool IsLeaseExpired(DateTime nowUtc) => Status == BillingClaimStatus.Pending && LeaseExpiresUtc <= nowUtc;

    /// <summary>Takes (or extends) the lease on a pending claim.</summary>
    public void Renew(DateTime nowUtc, TimeSpan lease)
    {
        LeaseExpiresUtc = nowUtc + lease;
        Touch(nowUtc);
    }

    public void Complete(long providerId, DateTime nowUtc)
    {
        ProviderId = providerId;
        Status = BillingClaimStatus.Completed;
        Touch(nowUtc);
    }

    private void Touch(DateTime nowUtc)
    {
        UpdatedUtc = nowUtc;
        Version++;
    }
}
