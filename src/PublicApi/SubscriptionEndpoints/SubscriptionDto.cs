using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's subscription, as reflected by the billing system of record (Maxio).
/// </summary>
public class SubscriptionDto
{
    public int Id { get; set; }

    /// <summary>Subscription state, e.g. <c>active</c>, <c>trialing</c>, <c>past_due</c>, <c>canceled</c>.</summary>
    public string State { get; set; } = string.Empty;

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    public int PriceInCents { get; set; }

    public decimal Price { get; set; }

    public string FormattedPrice { get; set; } = string.Empty;

    public string PaymentCollectionMethod { get; set; } = string.Empty;

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>The next billing date (Maxio <c>next_assessment_at</c>).</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }
}
