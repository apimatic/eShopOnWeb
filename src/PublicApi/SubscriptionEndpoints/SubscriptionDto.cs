using System;
using Microsoft.eShopWeb.PublicApi.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int? SubscriptionId { get; set; }
    public string? Reference { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public long? PriceInCents { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    /// <summary>Maxio subscription state as sent on the wire (e.g. <c>active</c>).</summary>
    public string? State { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }

    public static SubscriptionDto From(SubscriptionDetails details) => new()
    {
        SubscriptionId = details.SubscriptionId,
        Reference = details.Reference,
        PlanHandle = details.PlanHandle,
        PlanName = details.PlanName,
        PriceInCents = details.PriceInCents,
        Price = details.PriceInCents / 100m,
        Currency = details.Currency,
        Interval = details.Interval,
        IntervalUnit = details.IntervalUnit,
        State = details.State,
        NextBillingAt = details.NextBillingAt,
        CreatedAt = details.CreatedAt
    };
}
