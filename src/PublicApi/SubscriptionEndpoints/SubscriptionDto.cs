using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's subscription, as reported by the billing system of record.
/// </summary>
public class SubscriptionDto
{
    /// <summary>Identifier of the subscription in the billing system.</summary>
    public int Id { get; set; }

    /// <summary>Reference eShopOnWeb assigned to this subscription. Unique per billing site.</summary>
    public string? Reference { get; set; }

    /// <summary>Billing state, e.g. "active", "trialing", "past_due", "canceled".</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>True while the subscription still occupies its plan.</summary>
    public bool IsLive { get; set; }

    /// <summary>True while the shopper should have access to the paid service.</summary>
    public bool GrantsEntitlement { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    /// <summary>Recurring amount billed, in major units.</summary>
    public decimal Price { get; set; }

    public long PriceInCents { get; set; }

    public string Currency { get; set; } = string.Empty;

    public int? Interval { get; set; }

    public string? IntervalUnit { get; set; }

    /// <summary>Human readable cadence, e.g. "$299.00 / month".</summary>
    public string PriceDescription { get; set; } = string.Empty;

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the billing system will next attempt to collect.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Outstanding amount on the subscription, in major units.</summary>
    public decimal Balance { get; set; }

    /// <summary>How the billing system collects: "remittance"/"invoice" (invoiced) or "automatic" (charged).</summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>Identifier of the billing customer that stands in for the eShopOnWeb user.</summary>
    public int CustomerId { get; set; }

    /// <summary>Reference eShopOnWeb assigned to that billing customer.</summary>
    public string? CustomerReference { get; set; }
}
