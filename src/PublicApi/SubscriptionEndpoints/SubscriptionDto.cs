using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public decimal? Price { get; set; }
    public long? PriceInCents { get; set; }
    public string? Currency { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public string? State { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }

    public static SubscriptionDto From(SubscriptionDetails details) => new()
    {
        SubscriptionId = details.SubscriptionId,
        PlanHandle = details.PlanHandle,
        PlanName = details.PlanName,
        Price = details.PriceInCents / 100m,
        PriceInCents = details.PriceInCents,
        Currency = details.Currency,
        Interval = details.Interval,
        IntervalUnit = details.IntervalUnit,
        State = details.State,
        NextBillingDate = details.NextBillingAt,
        ActivatedAt = details.ActivatedAt,
        CreatedAt = details.CreatedAt
    };
}
