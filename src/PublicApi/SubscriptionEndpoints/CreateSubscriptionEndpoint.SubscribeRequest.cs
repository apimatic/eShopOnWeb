using System;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>Handle of the plan (Maxio product) to subscribe to, e.g. "eshop-pro".</summary>
    public string ProductHandle { get; set; } = string.Empty;
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
}
