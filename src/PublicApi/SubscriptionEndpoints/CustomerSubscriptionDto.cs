using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's subscription, as recorded in Maxio.
/// </summary>
public class CustomerSubscriptionDto
{
    public int Id { get; set; }

    /// <summary>Lifecycle state (e.g. "active").</summary>
    public string State { get; set; } = string.Empty;

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    public decimal Price { get; set; }

    public int PriceInCents { get; set; }

    public string? PaymentCollectionMethod { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the next charge/assessment is scheduled (the next billing date).</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }
}
