using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A subscribable plan, as returned by <c>GET /api/subscription-plans</c>.</summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    /// <summary>e.g. "$299.00/month".</summary>
    public string PriceDisplay { get; set; } = string.Empty;
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }

    public static SubscriptionPlanDto From(MaxioPlan p)
    {
        var price = p.PriceInCents / 100m;
        var unit = string.IsNullOrEmpty(p.IntervalUnit) ? "period" : p.IntervalUnit!;
        var every = p.Interval is > 1 ? $"{p.Interval} {unit}s" : unit;
        return new SubscriptionPlanDto
        {
            Handle = p.Handle,
            Name = p.Name,
            Description = p.Description,
            Price = price,
            PriceDisplay = $"{price:C}/{every}",
            Interval = p.Interval,
            IntervalUnit = p.IntervalUnit
        };
    }
}

/// <summary>A subscription, as returned by <c>GET /api/my-subscriptions</c> and <c>POST /api/subscriptions</c>.</summary>
public class SubscriptionDto
{
    public long Id { get; set; }
    public string? Reference { get; set; }
    public string State { get; set; } = string.Empty;
    /// <summary>Actionable classification: Active, Provisioning, or AttentionRequired.</summary>
    public string Status { get; set; } = string.Empty;
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public decimal? Price { get; set; }
    public string? PriceDisplay { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }

    public static SubscriptionDto From(MaxioSubscriptionView v)
    {
        decimal? price = v.PriceInCents.HasValue ? v.PriceInCents.Value / 100m : null;
        return new SubscriptionDto
        {
            Id = v.Id,
            Reference = v.Reference,
            State = v.State,
            Status = v.Outcome.ToString(),
            PlanHandle = v.ProductHandle,
            PlanName = v.ProductName,
            Price = price,
            PriceDisplay = price.HasValue ? $"{price.Value:C}" : null,
            CurrentPeriodEndsAt = v.CurrentPeriodEndsAt,
            NextBillingDate = v.NextBillingAt
        };
    }
}

/// <summary>Response for <c>GET /api/subscription-plans</c>.</summary>
public class SubscriptionPlansResponse : BaseResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

/// <summary>Response for <c>GET /api/my-subscriptions</c>.</summary>
public class MySubscriptionsResponse : BaseResponse
{
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}

/// <summary>Request body for <c>POST /api/subscriptions</c>.</summary>
public class SubscribeRequest : BaseRequest
{
    /// <summary>The handle of the plan to subscribe to (from <c>GET /api/subscription-plans</c>).</summary>
    public string? PlanHandle { get; set; }

    /// <summary>Resolved from the JWT by the endpoint; never bound from the request body.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public MaxioUserContext? User { get; set; }
}

/// <summary>Response for <c>POST /api/subscriptions</c>.</summary>
public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId) { }
    public SubscribeResponse() { }

    public SubscriptionDto Subscription { get; set; } = new();
    public bool AlreadySubscribed { get; set; }
    public string Message { get; set; } = string.Empty;
}
