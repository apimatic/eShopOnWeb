using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int IntervalCount { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;

    public static SubscriptionPlanDto From(SubscriptionPlan p) => new()
    {
        Handle = p.Handle,
        Name = p.Name,
        Description = p.Description,
        Price = p.Price,
        IntervalCount = p.IntervalCount,
        IntervalUnit = p.IntervalUnit
    };
}

public class SubscriptionDto
{
    public string SubscriptionId { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int IntervalCount { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; set; }

    public static SubscriptionDto From(SubscriptionInfo s) => new()
    {
        SubscriptionId = s.SubscriptionId,
        State = s.State,
        PlanHandle = s.PlanHandle,
        PlanName = s.PlanName,
        Price = s.Price,
        IntervalCount = s.IntervalCount,
        IntervalUnit = s.IntervalUnit,
        NextBillingDate = s.NextBillingDate
    };
}

public class ListSubscriptionPlansRequest : BaseRequest
{
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListSubscriptionPlansResponse()
    {
    }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to (see GET /api/subscription-plans).</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>Set from the JWT by the endpoint; never read from the body.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string UserName { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    public SubscriptionDto Subscription { get; set; } = new();

    /// <summary>False when the user already held this plan and the existing subscription is returned.</summary>
    public bool Created { get; set; }
}

public class ListMySubscriptionsRequest : BaseRequest
{
    public string UserName { get; set; } = string.Empty;
}

public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListMySubscriptionsResponse()
    {
    }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
