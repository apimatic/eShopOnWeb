using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>A subscribable plan (a product within the configured Maxio product family).</summary>
public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public decimal Price { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public string? PricePointHandle { get; set; }
}

/// <summary>A customer's subscription, as reflected back from Maxio.</summary>
public class SubscriptionDto
{
    public int Id { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public string? State { get; set; }
    public long PriceInCents { get; set; }
    public decimal Price { get; set; }
    /// <summary>Next billing date (Maxio <c>next_assessment_at</c>).</summary>
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
}

/// <summary>Outcome of a subscribe attempt: the resulting subscription plus whether it already existed.</summary>
public sealed record SubscribeOutcome(SubscriptionDto Subscription, bool AlreadySubscribed);
