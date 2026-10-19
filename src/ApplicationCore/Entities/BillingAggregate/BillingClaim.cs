using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// A claim recorded locally before a write to the billing provider. Its primary key is derived
/// deterministically from what the write is for, so a second caller's insert is refused by the store.
/// </summary>
public abstract class BillingClaim
{
    #pragma warning disable CS8618 // Required by Entity Framework
    protected BillingClaim() { }

    protected BillingClaim(string id, DateTimeOffset now)
    {
        Id = Guard.Against.NullOrWhiteSpace(id, nameof(id));
        State = BillingClaimState.Pending;
        LeaseId = Guid.NewGuid();
        ClaimedAt = now;
        UpdatedAt = now;
    }

    public string Id { get; private set; }
    public BillingClaimState State { get; protected set; }

    /// <summary>Concurrency token: changes every time the claim is taken over or settled.</summary>
    public Guid LeaseId { get; private set; }

    public DateTimeOffset ClaimedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>True when the claim is unresolved and nobody has touched it within <paramref name="lease"/>.</summary>
    public bool IsStale(DateTimeOffset now, TimeSpan lease) =>
        State != BillingClaimState.Active && now - UpdatedAt >= lease;

    /// <summary>Takes over an abandoned claim; the new lease id makes a concurrent take-over fail on save.</summary>
    public void Renew(DateTimeOffset now)
    {
        State = BillingClaimState.Pending;
        Touch(now);
    }

    public void MarkUnknown(DateTimeOffset now)
    {
        State = BillingClaimState.Unknown;
        Touch(now);
    }

    protected void Touch(DateTimeOffset now)
    {
        LeaseId = Guid.NewGuid();
        UpdatedAt = now;
    }
}
