using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// API projection of a customer's subscription.
/// </summary>
public class CustomerSubscriptionDto
{
    public long Id { get; set; }

    public string State { get; set; } = string.Empty;

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    public int PriceInCents { get; set; }

    public decimal Price { get; set; }

    /// <summary>End of the current billing period (the next scheduled charge date).</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the next payment capture will be attempted.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }
}
