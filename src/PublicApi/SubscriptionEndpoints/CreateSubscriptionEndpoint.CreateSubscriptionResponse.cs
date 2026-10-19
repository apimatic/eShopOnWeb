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

    /// <summary><c>created</c>, <c>already_subscribed</c>, <c>in_progress</c> or <c>unconfirmed</c>.</summary>
    public string Status { get; set; } = string.Empty;
    public string? Message { get; set; }
    public SubscriptionDto? Subscription { get; set; }
}
