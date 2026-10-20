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

    /// <summary><c>created</c>, <c>already_subscribed</c> or <c>pending</c>.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>The subscription reference sent to Maxio (stable per user and plan).</summary>
    public string Reference { get; set; } = string.Empty;

    public string? Message { get; set; }

    public SubscriptionDto? Subscription { get; set; }
}
