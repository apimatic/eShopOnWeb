using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.eShopWeb.PublicApi.Subscriptions.Maxio;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlansResponse
{
    public IReadOnlyList<SubscriptionPlanDto> Plans { get; init; } = Array.Empty<SubscriptionPlanDto>();
}

public sealed class SubscriptionPlanDto
{
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public long? PriceInCents { get; init; }
    public int? Interval { get; init; }
    public string? IntervalUnit { get; init; }
    public string? Currency { get; init; }
}

public sealed class SubscribeRequest
{
    [Required]
    public string PlanHandle { get; set; } = string.Empty;
}

public sealed class MySubscriptionsResponse
{
    public IReadOnlyList<SubscriptionDto> Subscriptions { get; init; } = Array.Empty<SubscriptionDto>();
}

public sealed class SubscribeResponse
{
    public SubscriptionDto Subscription { get; init; } = new();
}

public sealed class SubscriptionDto
{
    public long MaxioSubscriptionId { get; init; }
    public string? Reference { get; init; }
    public string PlanHandle { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public long? PriceInCents { get; init; }
    public string? Currency { get; init; }
    public string State { get; init; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }

    public static SubscriptionDto FromMaxio(MaxioSubscription subscription)
    {
        return new SubscriptionDto
        {
            MaxioSubscriptionId = subscription.Id,
            Reference = subscription.Reference,
            PlanHandle = subscription.Product?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? string.Empty,
            PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
            Currency = subscription.Currency,
            State = subscription.State,
            NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            ExpiresAt = subscription.ExpiresAt
        };
    }
}
