using System;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public long? PriceInCents { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }

    public static SubscriptionDto From(SubscriptionDetails subscription) => new()
    {
        SubscriptionId = subscription.SubscriptionId,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        Price = subscription.Price,
        Currency = subscription.Currency,
        Interval = subscription.Interval,
        IntervalUnit = subscription.IntervalUnit,
        State = subscription.State,
        NextBillingAt = subscription.NextBillingAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        CreatedAt = subscription.CreatedAt
    };
}
