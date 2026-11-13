using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using Microsoft.eShopWeb.PublicApi.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Description { get; set; }
    /// <summary>Recurring price in cents (e.g. 29900 = $299.00).</summary>
    public long? PriceInCents { get; set; }
    public decimal? Price => PriceInCents / 100m;
    public int? Interval { get; set; }
    /// <summary><c>month</c> or <c>day</c>.</summary>
    public string? IntervalUnit { get; set; }

    public static SubscriptionPlanDto From(PlanView plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit
    };
}

public class SubscriptionDto
{
    public long Id { get; set; }
    public string? Reference { get; set; }
    /// <summary>Maxio subscription state (e.g. <c>active</c>, <c>past_due</c>, <c>canceled</c>).</summary>
    public string State { get; set; } = string.Empty;
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    /// <summary>The subscribed recurring price in cents.</summary>
    public long? PriceInCents { get; set; }
    public decimal? Price => PriceInCents / 100m;
    public string? Currency { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public static SubscriptionDto From(SubscriptionView s) => new()
    {
        Id = s.Id,
        Reference = s.Reference,
        State = s.State,
        PlanHandle = s.PlanHandle,
        PlanName = s.PlanName,
        PriceInCents = s.PriceInCents,
        Currency = s.Currency,
        NextBillingAt = s.NextBillingAt,
        CurrentPeriodEndsAt = s.CurrentPeriodEndsAt
    };
}

/// <summary>Request context shared by the subscription endpoints: who is calling (from the JWT) and the request's cancellation.</summary>
public abstract class AuthenticatedBillingRequest : BaseRequest
{
    [JsonIgnore]
    public string UserName { get; set; } = string.Empty;

    [JsonIgnore]
    public CancellationToken RequestAborted { get; set; }
}

public class BillingErrorResponse : BaseResponse
{
    public BillingErrorResponse(Guid correlationId) : base(correlationId)
    {
    }

    public BillingErrorResponse()
    {
    }

    public int StatusCode { get; set; }
    public string Message { get; set; } = string.Empty;
    /// <summary>The billing provider's validation messages, when it rejected the request.</summary>
    public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();
}
