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

    /// <summary><c>created</c>, <c>already_subscribed</c> or <c>pending_confirmation</c>.</summary>
    public string Outcome { get; set; } = string.Empty;

    /// <summary>The subscription; null while the outcome is pending confirmation.</summary>
    public SubscriptionDto? Subscription { get; set; }

    public string SubscriptionReference { get; set; } = string.Empty;
}
