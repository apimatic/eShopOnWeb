using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListSubscriptionPlansResponse()
    {
    }

    public List<SubscriptionPlan> Plans { get; set; } = new();

    /// <summary>True when the plan listing hit its page cap, so <see cref="Plans"/> may be incomplete.</summary>
    public bool IsTruncated { get; set; }
}

public class SubscribeRequest : BaseRequest
{
    /// <summary>Handle of a plan returned by GET api/subscription-plans.</summary>
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

    public SubscriptionSummary? Subscription { get; set; }

    /// <summary>True when the user already had this subscription and this call created nothing.</summary>
    public bool AlreadySubscribed { get; set; }
}

public class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public MySubscriptionsResponse()
    {
    }

    public List<SubscriptionSummary> Subscriptions { get; set; } = new();
}
