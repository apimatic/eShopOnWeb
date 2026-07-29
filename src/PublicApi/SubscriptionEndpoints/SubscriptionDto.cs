using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription belonging to the calling user, as recorded in Maxio Advanced Billing.
/// </summary>
public class SubscriptionDto
{
    /// <summary>Maxio subscription id.</summary>
    public int Id { get; set; }

    /// <summary>Subscription state, e.g. "active".</summary>
    public string State { get; set; } = string.Empty;

    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;

    public int PriceInCents { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";

    public string IntervalUnit { get; set; } = "month";
    public int IntervalCount { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the next charge will be assessed.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }

    public string? PaymentCollectionMethod { get; set; }

    public int CustomerId { get; set; }
    public string? CustomerReference { get; set; }
}
