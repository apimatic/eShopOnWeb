using System;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public decimal? Price { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static SubscriptionDto FromMaxioSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        Price = subscription.Product is null ? null : subscription.Product.PriceInCents / 100m,
        Interval = subscription.Product?.Interval,
        IntervalUnit = subscription.Product?.IntervalUnit,
        NextBillingAt = subscription.NextAssessmentAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        CreatedAt = subscription.CreatedAt
    };
}
