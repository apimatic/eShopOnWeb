using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper enrollment in a subscription plan, as held by the billing system of record.
/// </summary>
public class SubscriptionDto
{
    /// <summary>Identifier assigned by the billing system.</summary>
    public long Id { get; set; }

    /// <summary>Reference this application assigned to the subscription; also its idempotency key.</summary>
    public string? Reference { get; set; }

    /// <summary>Lifecycle state, for example <c>active</c>, <c>trialing</c> or <c>canceled</c>.</summary>
    public string State { get; set; } = string.Empty;

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    /// <summary>Price of one billing period, in the smallest unit of <see cref="Currency"/>.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Price of one billing period as a major-unit amount, for example 299.00.</summary>
    public decimal Price { get; set; }

    public string? Currency { get; set; }

    public int? Interval { get; set; }

    public string? IntervalUnit { get; set; }

    /// <summary>Outstanding balance, in the smallest unit of <see cref="Currency"/>.</summary>
    public long BalanceInCents { get; set; }

    /// <summary>How the billing system collects payment, for example <c>remittance</c>.</summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>Identifier of the billing-system customer that owns this subscription.</summary>
    public long CustomerId { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the subscription is next billed.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? TrialEndedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }
}
