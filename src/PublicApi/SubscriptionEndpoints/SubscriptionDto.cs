using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A shopper's enrolment in a plan.</summary>
public class SubscriptionDto
{
    public int? SubscriptionId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public long PriceInCents { get; set; }
    public string Currency { get; set; } = "USD";

    /// <summary>Subscription state, e.g. "active", "trialing".</summary>
    public string State { get; set; } = string.Empty;

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    /// <summary>When the current billing period ends — i.e. the next billing date.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    /// <summary>Human-readable price, e.g. "$299.00".</summary>
    public string PriceDisplay { get; set; } = string.Empty;
}
