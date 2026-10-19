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

    /// <summary><c>created</c>, <c>existing</c> (already subscribed — nothing new was created) or <c>pending</c> (outcome being confirmed).</summary>
    public string Status { get; set; } = string.Empty;

    public SubscriptionDto? Subscription { get; set; }

    public string? Message { get; set; }
}
