using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A shopper's subscription, as confirmed by the billing system.</summary>
public class CustomerSubscriptionDto
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public string FormattedPrice { get; set; } = string.Empty;

    /// <summary>End of the current billing period — the next billing date.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }
}
