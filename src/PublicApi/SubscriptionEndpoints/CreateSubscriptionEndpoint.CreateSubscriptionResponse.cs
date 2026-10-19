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

    /// <summary><c>created</c>, <c>already_subscribed</c> or <c>pending</c> (outcome being confirmed with the billing provider).</summary>
    public string Status { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    /// <summary>The reference sent to the billing provider for this enrollment.</summary>
    public string? SubscriptionReference { get; set; }

    public SubscriptionDto? Subscription { get; set; }
}
