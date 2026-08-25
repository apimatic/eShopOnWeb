using System;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}

public class SubscriptionDto
{
    public long SubscriptionId { get; set; }
    public string? Reference { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}

public static class SubscriptionMapper
{
    public static SubscriptionPlanDto ToDto(MaxioProduct product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Price = product.PriceInCents / 100m,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit
    };

    public static SubscriptionDto ToDto(MaxioSubscription subscription) => new()
    {
        SubscriptionId = subscription.Id,
        Reference = subscription.Reference,
        State = subscription.State,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents,
        Price = subscription.ProductPriceInCents / 100m,
        Interval = subscription.Product?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit ?? string.Empty,
        // The next regularly scheduled charge; next_assessment_at diverges only during dunning retries.
        NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
        ActivatedAt = subscription.ActivatedAt,
        CreatedAt = subscription.CreatedAt
    };
}
