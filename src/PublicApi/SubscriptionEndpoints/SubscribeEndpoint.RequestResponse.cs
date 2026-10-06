using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>Stable handle of the plan to subscribe to (e.g. "eshop-pro").</summary>
    public string PlanHandle { get; set; } = string.Empty;
}

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId) { }
    public SubscribeResponse() { }

    public MySubscriptionDto Subscription { get; set; } = new();

    /// <summary>True when the caller already had a live subscription to this plan; no new one was created.</summary>
    public bool AlreadySubscribed { get; set; }

    public string Message { get; set; } = string.Empty;
}
