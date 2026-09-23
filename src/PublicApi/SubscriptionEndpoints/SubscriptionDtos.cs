using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A subscribable plan.</summary>
public record SubscriptionPlanDto
{
    public string Handle { get; init; } = string.Empty;
    public string? Name { get; init; }
    public string? Description { get; init; }
    public long PriceInCents { get; init; }
    public string Price { get; init; } = string.Empty;
    public int? Interval { get; init; }
    public string? IntervalUnit { get; init; }
    public int? ProductId { get; init; }
}

/// <summary>Response for <c>GET /api/subscription-plans</c>.</summary>
public record SubscriptionPlansResponse
{
    public IReadOnlyList<SubscriptionPlanDto> Plans { get; init; } = Array.Empty<SubscriptionPlanDto>();

    /// <summary>True when the list was capped by the page size and may be incomplete.</summary>
    public bool Truncated { get; init; }
}

/// <summary>Request body for <c>POST /api/subscriptions</c>. Omit <see cref="PlanHandle"/> to use the configured default plan.</summary>
public record SubscribeRequest
{
    public string? PlanHandle { get; init; }
}

/// <summary>Response for <c>POST /api/subscriptions</c>.</summary>
public record SubscribeResponse
{
    public string Outcome { get; init; } = string.Empty;
    public string PlanHandle { get; init; } = string.Empty;
    public string? PlanName { get; init; }
    public long PriceInCents { get; init; }
    public string Price { get; init; } = string.Empty;
    public string? State { get; init; }
    public DateTimeOffset? NextBillingDate { get; init; }
    public int SubscriptionId { get; init; }
    public int CustomerId { get; init; }
    public string Reference { get; init; } = string.Empty;
}

/// <summary>A subscription owned by the current buyer.</summary>
public record MySubscriptionDto
{
    public int SubscriptionId { get; init; }
    public string? PlanHandle { get; init; }
    public string? PlanName { get; init; }
    public long PriceInCents { get; init; }
    public string Price { get; init; } = string.Empty;
    public string? State { get; init; }
    public DateTimeOffset? NextBillingDate { get; init; }
    public string? Reference { get; init; }
}

/// <summary>Response for <c>GET /api/my-subscriptions</c>.</summary>
public record MySubscriptionsResponse
{
    public IReadOnlyList<MySubscriptionDto> Subscriptions { get; init; } = Array.Empty<MySubscriptionDto>();
}

/// <summary>A caller-safe error payload.</summary>
public record SubscriptionErrorResponse(string Error);
