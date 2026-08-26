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

    public SubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// True when a new subscription was created; false when the user was already
    /// subscribed and the existing subscription is returned.
    /// </summary>
    public bool Created { get; set; }

    public string? Error { get; set; }
}
