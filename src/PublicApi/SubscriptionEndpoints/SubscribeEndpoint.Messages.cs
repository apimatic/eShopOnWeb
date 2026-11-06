using System;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>Handle of one of the plans returned by GET api/subscription-plans.</summary>
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

    /// <summary>True when the user already had a live subscription to the plan, so nothing new was created.</summary>
    public bool AlreadySubscribed { get; set; }
}
