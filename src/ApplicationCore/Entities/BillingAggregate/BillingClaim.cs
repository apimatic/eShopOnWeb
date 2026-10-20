using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// A row that a request inserts BEFORE it asks the billing provider to create something.
/// Its string primary key is the deterministic reference sent to the provider, so a second
/// request for the same thing is refused by the store (duplicate primary key) before it can
/// reach the provider. <see cref="Version"/> is an optimistic-concurrency token used when a
/// request takes over a stale or unknown claim, so only one request can take it over.
/// </summary>
public abstract class BillingClaim
{
    protected BillingClaim()
    {
        Key = string.Empty;
    }

    protected BillingClaim(string key, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("A claim key is required.", nameof(key));
        Key = key;
        State = BillingClaimState.Pending;
        Version = Guid.NewGuid();
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string Key { get; private set; }
    public BillingClaimState State { get; private set; }
    public Guid Version { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>True when the claim is Pending but older than <paramref name="maxPendingAge"/> (its owner is gone).</summary>
    public bool IsStalePending(DateTimeOffset now, TimeSpan maxPendingAge) =>
        State == BillingClaimState.Pending && now - UpdatedAt > maxPendingAge;

    /// <summary>Re-takes an Unknown or stale Pending claim for the current request.</summary>
    public void TakeOver(DateTimeOffset now) => Transition(BillingClaimState.Pending, now);

    public void MarkUnknown(DateTimeOffset now) => Transition(BillingClaimState.Unknown, now);

    protected void MarkCompletedCore(DateTimeOffset now) => Transition(BillingClaimState.Completed, now);

    private void Transition(BillingClaimState state, DateTimeOffset now)
    {
        State = state;
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }
}
