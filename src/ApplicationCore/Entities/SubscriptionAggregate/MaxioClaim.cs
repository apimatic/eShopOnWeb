using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public enum ClaimState
{
    /// <summary>Claim taken; the billing-system call has not been recorded yet.</summary>
    Claimed,
    /// <summary>The billing-system entity exists and its id is recorded.</summary>
    Active,
    /// <summary>The billing-system call may have landed; to be settled by reconciling on <see cref="MaxioClaim.Reference"/>.</summary>
    Unknown
}

/// <summary>
/// Local record of a write made to the billing system. The row's primary key is the claim:
/// a second concurrent request for the same key is refused by the store.
/// </summary>
public abstract class MaxioClaim
{
    /// <summary>Deterministic reference sent to the billing system; used to reconcile unknown outcomes.</summary>
    public string Reference { get; set; } = string.Empty;
    public int? MaxioId { get; set; }
    public ClaimState State { get; set; } = ClaimState.Claimed;
    /// <summary>Doubles as the concurrency token so only one caller can take over a stale claim.</summary>
    public DateTimeOffset ClaimedAt { get; set; } = DateTimeOffset.UtcNow;
}
