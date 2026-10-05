using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription the caller holds in Maxio Advanced Billing.
/// </summary>
public class SubscriptionDto
{
    /// <summary>Maxio subscription id (the billing system of record id).</summary>
    public int SubscriptionId { get; set; }

    /// <summary>Subscription state per the Maxio spec (e.g. "active", "trialing", "canceled").</summary>
    public string? State { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    /// <summary>Recurring price in integer cents, as recorded on the subscription.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price in major units (e.g. 299.00).</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>When the current billing period ends (the next billing date).</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    /// <summary>When the next payment attempt is scheduled.</summary>
    public DateTimeOffset? NextAssessmentAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public bool? CancelAtEndOfPeriod { get; set; }

    /// <summary>Maxio customer id the subscription belongs to.</summary>
    public int MaxioCustomerId { get; set; }

    /// <summary>App-provided subscription reference (deterministic per user and plan).</summary>
    public string? Reference { get; set; }
}