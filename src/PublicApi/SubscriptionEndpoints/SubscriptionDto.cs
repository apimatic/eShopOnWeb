using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's subscription, projected for the API.
/// </summary>
public class SubscriptionDto
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public string FormattedPrice { get; set; } = string.Empty;
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the next billing/assessment will occur.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }
    public string PaymentCollectionMethod { get; set; } = string.Empty;
}
