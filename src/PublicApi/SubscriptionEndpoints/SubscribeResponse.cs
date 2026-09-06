using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public bool Success { get; set; }
    public SubscriptionDto? Subscription { get; set; }
    public string? ErrorMessage { get; set; }
}
