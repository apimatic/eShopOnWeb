using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's subscription, as recorded by the billing system of record.
/// </summary>
public class SubscriptionDto
{
    /// <summary>Identifier of the subscription in the billing system.</summary>
    public int Id { get; set; }

    /// <summary>Reference eShopOnWeb assigned to the subscription.</summary>
    public string? Reference { get; set; }

    /// <summary>Billing state, e.g. <c>active</c>, <c>trialing</c>, <c>past_due</c>, <c>canceled</c>.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>True while the subscription is a live engagement.</summary>
    public bool IsActive { get; set; }

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public long PriceInCents { get; set; }

    public string Currency { get; set; } = string.Empty;

    public int Interval { get; set; }

    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Human readable billing cadence, e.g. "$299.00 USD / month".</summary>
    public string BillingSummary { get; set; } = string.Empty;

    /// <summary>When the subscription will next be billed.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Outstanding balance on the subscription.</summary>
    public decimal Balance { get; set; }

    /// <summary>How the billing system collects payment, e.g. <c>remittance</c>.</summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>Identifier of the billing-system customer that owns the subscription.</summary>
    public int CustomerId { get; set; }

    /// <summary>Reference eShopOnWeb assigned to the billing-system customer.</summary>
    public string? CustomerReference { get; set; }
}
