using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public UserSubscriptionDto? Subscription { get; set; }
}
