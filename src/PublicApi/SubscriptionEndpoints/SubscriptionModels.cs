using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A subscription plan the shopper can enroll in.</summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Recurring price in integer cents.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price in major currency units (derived from <see cref="PriceInCents"/>).</summary>
    public decimal Price { get; set; }

    public int? IntervalCount { get; set; }
    public string? IntervalUnit { get; set; }
    public int? ProductId { get; set; }
}

public class SubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

/// <summary>POST body for <c>POST /api/subscriptions</c>. The caller's identity comes from the JWT, not the body.</summary>
public class SubscribeSubscriptionRequest
{
    /// <summary>The API handle of the plan to subscribe to (e.g. <c>eshop-pro</c>).</summary>
    public string PlanHandle { get; set; } = string.Empty;
}

/// <summary>A shopper's subscription as reported by the billing provider.</summary>
public class SubscriptionDto
{
    public int Id { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public long? PriceInCents { get; set; }
    public decimal? Price { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}

public class SubscribeResponse
{
    public SubscriptionDto Subscription { get; set; } = new();

    /// <summary>False when an existing live subscription to the same plan was returned instead of creating a new one.</summary>
    public bool WasCreated { get; set; }
}

public class MySubscriptionsResponse
{
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
