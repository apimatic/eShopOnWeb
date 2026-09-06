using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Response for GET /api/subscription-plans.</summary>
public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListSubscriptionPlansResponse()
    {
    }

    /// <summary>Plans on offer, cheapest first.</summary>
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

/// <summary>Response for POST /api/subscriptions.</summary>
public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscribeResponse()
    {
    }

    /// <summary>The shopper's subscription to the requested plan.</summary>
    public SubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// True when the shopper already held a live subscription to this plan and nothing new was
    /// created. The endpoint answers 200 OK in that case and 201 Created otherwise.
    /// </summary>
    public bool AlreadySubscribed { get; set; }

    /// <summary>Short confirmation suitable for showing to the shopper.</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>Response for GET /api/my-subscriptions.</summary>
public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListMySubscriptionsResponse()
    {
    }

    /// <summary>Every subscription held by the caller, newest first.</summary>
    public List<SubscriptionDto> Subscriptions { get; set; } = new();

    /// <summary>The subset of <see cref="Subscriptions"/> that currently entitle the caller.</summary>
    public int ActiveCount { get; set; }
}
