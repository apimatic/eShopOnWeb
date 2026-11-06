using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public class PlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Description { get; set; }
    public long? PriceInCents { get; set; }
    public decimal? Price { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
}

public class PlanListing
{
    public IReadOnlyList<PlanDto> Plans { get; init; } = Array.Empty<PlanDto>();

    /// <summary>True when the page cap was reached while the last page was still full.</summary>
    public bool Truncated { get; init; }
}

public class SubscriptionDto
{
    public int? Id { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public long? PriceInCents { get; set; }
    public decimal? Price { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public string? State { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
}

public record SubscribeOutcome(SubscriptionDto Subscription, bool Created);
