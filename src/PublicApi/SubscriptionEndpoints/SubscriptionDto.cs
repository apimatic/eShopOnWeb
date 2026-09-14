using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A shopper's enrollment in a plan, as held by the billing system of record.</summary>
public class SubscriptionDto
{
    public int Id { get; set; }

    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;

    /// <summary>Provider state, e.g. <c>active</c>, <c>trialing</c>, <c>canceled</c>.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>True while the provider still considers this enrollment live.</summary>
    public bool IsLive { get; set; }

    public long PriceInCents { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string FormattedPrice { get; set; } = string.Empty;

    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public string BillingPeriod { get; set; } = string.Empty;

    /// <summary>When the provider will next bill this subscription. Null once it is no longer live.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }

    public long BalanceInCents { get; set; }
    public decimal Balance { get; set; }
}
