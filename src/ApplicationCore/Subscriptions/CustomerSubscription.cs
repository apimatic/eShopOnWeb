using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A customer's subscription as reported by Maxio (the system of record), projected onto a
/// plain domain DTO. Values reflect live Maxio state at read time.
/// </summary>
public class CustomerSubscription
{
    /// <summary>The Maxio subscription id.</summary>
    public int SubscriptionId { get; init; }

    /// <summary>The subscribed product/plan handle, when present in the Maxio payload.</summary>
    public string? PlanHandle { get; init; }

    /// <summary>The subscribed product/plan name, when present in the Maxio payload.</summary>
    public string? PlanName { get; init; }

    /// <summary>The raw Maxio subscription state wire value (e.g. <c>active</c>, <c>trialing</c>, <c>past_due</c>).</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>True only for live states (<c>active</c>/<c>trialing</c>); every other state is reported verbatim, not masked.</summary>
    public bool IsActive { get; init; }

    public long? PriceInCents { get; init; }

    public string? PriceFormatted { get; init; }

    /// <summary>
    /// When the next regularly scheduled charge occurs (Maxio <c>current_period_ends_at</c>) — the "next billing date".
    /// Null for states that have no upcoming billing (e.g. canceled/expired).
    /// </summary>
    public DateTimeOffset? NextBillingDate { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>The deterministic app-supplied reference that keys idempotency for this subscription.</summary>
    public string? Reference { get; init; }
}
