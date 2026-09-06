using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's subscription as held by the billing system.
/// </summary>
public class SubscriptionDto
{
    /// <summary>Identifier assigned by the billing system.</summary>
    public long Id { get; set; }

    /// <summary>Reference this application stamped on the subscription; also its idempotency key.</summary>
    public string? Reference { get; set; }

    /// <summary>Lifecycle state, for example "active", "past_due" or "canceled".</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>True while the subscription still entitles the shopper to the plan.</summary>
    public bool IsLive { get; set; }

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public long PriceInCents { get; set; }

    public string Currency { get; set; } = "USD";

    public int Interval { get; set; }

    public string IntervalUnit { get; set; } = string.Empty;

    public string BillingPeriod { get; set; } = string.Empty;

    public DateTimeOffset? CurrentPeriodStartsAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the billing system will next bill this subscription.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? TrialEndsAt { get; set; }

    /// <summary>How the recurring charge is collected, for example "remittance" or "automatic".</summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>Outstanding balance, in the smallest currency unit.</summary>
    public long BalanceInCents { get; set; }

    /// <summary>Identifier of the billing customer that owns the subscription.</summary>
    public long CustomerId { get; set; }

    /// <summary>Reference this application stamped on the billing customer.</summary>
    public string? CustomerReference { get; set; }
}
