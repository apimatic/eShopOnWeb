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

    /// <summary>created, existing (already subscribed) or pending (outcome being confirmed; repeat the request).</summary>
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public SubscriptionDto? Subscription { get; set; }
}
