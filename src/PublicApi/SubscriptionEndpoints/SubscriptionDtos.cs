using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Description { get; set; }
    public long? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }

    public static SubscriptionPlanDto From(PlanInfo p) => new()
    {
        Handle = p.Handle,
        Name = p.Name,
        Description = p.Description,
        PriceInCents = p.PriceInCents,
        Interval = p.Interval,
        IntervalUnit = p.IntervalUnit
    };
}

public class SubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public long? PriceInCents { get; set; }
    public string? Currency { get; set; }
    /// <summary>Provider subscription state as its wire value, e.g. "active".</summary>
    public string? State { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }

    public static SubscriptionDto From(SubscriptionInfo s) => new()
    {
        SubscriptionId = s.SubscriptionId,
        PlanHandle = s.PlanHandle,
        PlanName = s.PlanName,
        PriceInCents = s.PriceInCents,
        Currency = s.Currency,
        State = s.State,
        NextBillingDate = s.NextBillingDate,
        CreatedAt = s.CreatedAt
    };
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
    /// <summary>True when the listing was cut short and is not the complete catalog.</summary>
    public bool IsTruncated { get; set; }
}

public class SubscribeRequest : BaseRequest
{
    /// <summary>Plan handle to subscribe to. Defaults to the standard plan when omitted.</summary>
    public string? PlanHandle { get; set; }
}

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscribeResponse()
    {
    }

    public SubscriptionDto? Subscription { get; set; }
    /// <summary>False when the caller already had this subscription (idempotent repeat).</summary>
    public bool Created { get; set; }
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
