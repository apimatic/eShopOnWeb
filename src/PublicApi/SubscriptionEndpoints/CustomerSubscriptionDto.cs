using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's subscription, as reported by the billing system of record.
/// </summary>
public class CustomerSubscriptionDto
{
    /// <summary>The billing provider's identifier for the subscription.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Provider state, e.g. "active", "trialing", "past_due", "canceled".</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>True while the subscription still entitles the shopper to the plan.</summary>
    public bool IsActive { get; set; }

    /// <summary>The idempotency reference eShopOnWeb wrote when creating the subscription.</summary>
    public string? Reference { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    /// <summary>Recurring price in major units, e.g. 299.00.</summary>
    public decimal Price { get; set; }

    public long PriceInCents { get; set; }

    public string Currency { get; set; } = string.Empty;

    /// <summary>Ready-to-display price, e.g. "299.00 USD / month".</summary>
    public string FormattedPrice { get; set; } = string.Empty;

    public int? BillingIntervalLength { get; set; }

    public string? BillingIntervalUnit { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the provider will next bill this subscription.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Outstanding balance in minor units.</summary>
    public long BalanceInCents { get; set; }

    /// <summary>How the provider collects payment, e.g. "remittance" (invoiced) or "automatic" (card on file).</summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>The billing provider's identifier for the customer that owns this subscription.</summary>
    public string CustomerId { get; set; } = string.Empty;
}
