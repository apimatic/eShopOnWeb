using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's subscription as exposed on the API. <see cref="NextBillingDate"/> is the end of the
/// current billing period (the effective next-charge date).
/// </summary>
public class CustomerSubscriptionDto
{
    public int SubscriptionId { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    public decimal Price { get; set; }

    public long PriceInCents { get; set; }

    public string? State { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? NextBillingDate { get; set; }

    public string? CustomerReference { get; set; }
}
