using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's subscription, as held by the billing system of record.
/// </summary>
public class SubscriptionDto
{
    public long SubscriptionId { get; set; }

    /// <summary>e.g. <c>active</c>, <c>trialing</c>, <c>past_due</c>, <c>canceled</c>.</summary>
    public string State { get; set; } = string.Empty;

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    public decimal Price { get; set; }

    public long PriceInCents { get; set; }

    public string Currency { get; set; } = string.Empty;

    public string? BillingPeriod { get; set; }

    /// <summary>When the next renewal will be charged.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public decimal BalanceDue { get; set; }

    public string? PaymentCollectionMethod { get; set; }

    /// <summary>Identifiers of the billing record, handy when reconciling against the billing site.</summary>
    public long BillingCustomerId { get; set; }

    public string? BillingCustomerReference { get; set; }
}
