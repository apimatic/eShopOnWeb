using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Billing;

/// <summary>
/// The state of one of a user's recurring subscriptions, as recorded by the
/// billing system (Maxio Advanced Billing is the system of record).
/// </summary>
public class SubscriptionInfo
{
    public int SubscriptionId { get; set; }
    /// <summary>The client-supplied idempotency reference of the subscription.</summary>
    public string? Reference { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    /// <summary>Recurring price in cents (the plan's product price).</summary>
    public long? ProductPriceInCents { get; set; }
    /// <summary>Total billed amount in cents for the current period.</summary>
    public long? CurrentBillingAmountInCents { get; set; }
    /// <summary>Provider subscription state, e.g. "active", "canceled", "trialing".</summary>
    public string State { get; set; } = string.Empty;
    /// <summary>When the next billing event occurs (the current period's end).</summary>
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
}
