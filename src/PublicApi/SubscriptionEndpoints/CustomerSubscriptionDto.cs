using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>API projection of a shopper's subscription as recorded by Maxio.</summary>
public class CustomerSubscriptionDto
{
    public int Id { get; set; }

    /// <summary>Subscription state, e.g. "active".</summary>
    public string State { get; set; } = string.Empty;

    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;

    public long PriceInCents { get; set; }
    public string PriceFormatted { get; set; } = string.Empty;

    /// <summary>End of the current billing period / next billing date. Null if unbounded.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }
}
