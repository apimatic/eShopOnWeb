using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's enrollment in a <see cref="SubscriptionPlanDto"/>.
/// </summary>
public class SubscriptionDto
{
    /// <summary>Identifier of the subscription in the billing system of record.</summary>
    public long Id { get; set; }

    /// <summary>Lifecycle state, e.g. "active", "trialing", "past_due", "canceled".</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>True while the subscription still entitles the shopper to the plan.</summary>
    public bool IsLive { get; set; }

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public long PriceInCents { get; set; }

    public string Currency { get; set; } = string.Empty;

    public int Interval { get; set; }

    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Human readable price, e.g. "USD 299.00 / month".</summary>
    public string DisplayPrice { get; set; } = string.Empty;

    /// <summary>When the next renewal is scheduled. Null once the subscription stops renewing.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Outstanding balance in the smallest currency unit.</summary>
    public long BalanceInCents { get; set; }

    /// <summary>How the recurring charge is collected, e.g. "automatic" or "remittance".</summary>
    public string PaymentCollectionMethod { get; set; } = string.Empty;

    /// <summary>Identifier of the shopper's customer record in the billing system of record.</summary>
    public long CustomerId { get; set; }

    /// <summary>The eShopOnWeb-owned key that links that customer back to this user.</summary>
    public string? CustomerReference { get; set; }
}
