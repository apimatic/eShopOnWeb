using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's subscription, as reported by the billing system of record.
/// </summary>
public class SubscriptionDto
{
    /// <summary>Identifier of the subscription in the billing system.</summary>
    public int Id { get; set; }

    /// <summary>Billing state, e.g. <c>active</c>, <c>trialing</c>, <c>past_due</c>, <c>canceled</c>.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>True while the subscription still entitles the shopper.</summary>
    public bool IsLive { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    public long PriceInCents { get; set; }

    /// <summary>Recurring price as a decimal amount, e.g. <c>299.00</c>.</summary>
    public string Price { get; set; } = string.Empty;

    public string? Currency { get; set; }

    /// <summary>Human-readable billing period, e.g. <c>month</c>.</summary>
    public string? BillingPeriod { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the shopper will next be billed.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Outstanding balance in cents.</summary>
    public long BalanceInCents { get; set; }

    public string? PaymentCollectionMethod { get; set; }

    /// <summary>Billing customer this subscription belongs to.</summary>
    public BillingCustomerDto? Customer { get; set; }
}
