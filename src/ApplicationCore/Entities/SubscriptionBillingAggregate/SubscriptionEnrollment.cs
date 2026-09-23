using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBillingAggregate;

/// <summary>
/// Local record of a subscribe attempt. It is written <b>before</b> the Maxio CreateSubscription call
/// (carrying the deterministic <see cref="Reference"/> that the provider call also receives) and updated
/// after it returns. <see cref="Reference"/> carries a unique index so a concurrent double-click is
/// rejected by the store rather than by an in-process lock, and it is the key we re-read Maxio by when a
/// write's outcome is unknown.
/// </summary>
public class SubscriptionEnrollment : BaseEntity, IAggregateRoot
{
    public string BuyerId { get; private set; }
    public string PlanHandle { get; private set; }

    /// <summary>Deterministic, app-supplied reference; also sent to Maxio as the subscription reference.</summary>
    public string Reference { get; private set; }

    public int MaxioCustomerId { get; private set; }

    /// <summary>The Maxio subscription id, once the provider has acknowledged creation.</summary>
    public int? MaxioSubscriptionId { get; private set; }

    /// <summary>The last known Maxio subscription state (wire value, e.g. <c>active</c>). Null until confirmed.</summary>
    public string? State { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }

    public bool IsConfirmed => MaxioSubscriptionId.HasValue;

    public SubscriptionEnrollment(string buyerId, string planHandle, string reference, int maxioCustomerId)
    {
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        PlanHandle = Guard.Against.NullOrEmpty(planHandle, nameof(planHandle));
        Reference = Guard.Against.NullOrEmpty(reference, nameof(reference));
        MaxioCustomerId = Guard.Against.NegativeOrZero(maxioCustomerId, nameof(maxioCustomerId));
        CreatedAt = DateTimeOffset.UtcNow;
    }

    // Required by EF Core.
    private SubscriptionEnrollment()
    {
        BuyerId = string.Empty;
        PlanHandle = string.Empty;
        Reference = string.Empty;
    }

    /// <summary>Records that the Maxio subscription now exists (or has been adopted).</summary>
    public void Confirm(int maxioSubscriptionId, string? state)
    {
        MaxioSubscriptionId = Guard.Against.NegativeOrZero(maxioSubscriptionId, nameof(maxioSubscriptionId));
        State = state;
        ConfirmedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateState(string? state) => State = state;
}
