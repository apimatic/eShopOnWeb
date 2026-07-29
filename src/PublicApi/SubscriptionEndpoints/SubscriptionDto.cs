using System;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>API projection of a Maxio subscription belonging to the caller.</summary>
public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public decimal Price { get; set; }
    public long PriceInCents { get; set; }
    public string? IntervalUnit { get; set; }

    /// <summary>The next scheduled billing/renewal date (end of the current period).</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }

    public static SubscriptionDto FromDomain(SubscriptionSummary subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.ProductHandle,
        PlanName = subscription.ProductName,
        Price = subscription.ProductPrice,
        PriceInCents = subscription.ProductPriceInCents,
        IntervalUnit = subscription.IntervalUnit,
        NextBillingDate = subscription.NextBillingAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CreatedAt = subscription.CreatedAt
    };
}
