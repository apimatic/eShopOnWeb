using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// A claim on a billing-provider write (creating a customer or a subscription), keyed by the
/// deterministic reference that is also sent to the provider. The key is the primary key, so the
/// store itself refuses a second claim for the same write - this is what stops a double-click from
/// reaching the provider twice.
/// </summary>
public class BillingClaim : IAggregateRoot
{
    public string Key { get; private set; }
    public BillingClaimKind Kind { get; private set; }
    public BillingClaimStatus Status { get; private set; }

    /// <summary>The provider's id for the created resource, once known.</summary>
    public int? ProviderId { get; private set; }

    /// <summary>Changes on every claim/take-over; used as an optimistic concurrency token.</summary>
    public Guid ClaimToken { get; private set; }

    public DateTimeOffset ClaimedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    #pragma warning disable CS8618 // Required by Entity Framework
    private BillingClaim() { }

    public BillingClaim(string key, BillingClaimKind kind, DateTimeOffset now)
    {
        Guard.Against.NullOrWhiteSpace(key, nameof(key));
        Key = key;
        Kind = kind;
        Status = BillingClaimStatus.Pending;
        ClaimToken = Guid.NewGuid();
        ClaimedAt = now;
        UpdatedAt = now;
    }

    public bool IsStale(DateTimeOffset now, TimeSpan pendingLease) =>
        Status == BillingClaimStatus.Unknown ||
        (Status == BillingClaimStatus.Pending && now - ClaimedAt > pendingLease);

    public void TakeOver(DateTimeOffset now)
    {
        Status = BillingClaimStatus.Pending;
        ClaimToken = Guid.NewGuid();
        ClaimedAt = now;
        UpdatedAt = now;
    }

    public void Complete(int providerId, DateTimeOffset now)
    {
        Status = BillingClaimStatus.Completed;
        ProviderId = providerId;
        UpdatedAt = now;
    }

    public void MarkUnknown(DateTimeOffset now)
    {
        Status = BillingClaimStatus.Unknown;
        UpdatedAt = now;
    }
}

public enum BillingClaimKind
{
    Customer = 1,
    Subscription = 2
}

public enum BillingClaimStatus
{
    /// <summary>A request holds the claim and is talking to the provider.</summary>
    Pending = 1,

    /// <summary>The provider holds the resource; <see cref="BillingClaim.ProviderId"/> is set.</summary>
    Completed = 2,

    /// <summary>The provider may or may not have acted (connection lost mid-write); settle by re-reading.</summary>
    Unknown = 3
}
