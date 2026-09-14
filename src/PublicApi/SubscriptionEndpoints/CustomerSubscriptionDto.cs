using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's subscription as reported by Maxio, presented to API clients.
/// </summary>
public class CustomerSubscriptionDto
{
    public long Id { get; set; }

    public string State { get; set; } = string.Empty;

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    public int PriceInCents { get; set; }

    public decimal Price { get; set; }

    public string Currency { get; set; } = string.Empty;

    public string FormattedPrice { get; set; } = string.Empty;

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When Maxio will next bill this subscription.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public int CustomerId { get; set; }

    public string? CustomerReference { get; set; }
}
