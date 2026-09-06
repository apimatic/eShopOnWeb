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

    /// <summary>
    /// False when an equivalent subscription already existed, so this request changed nothing.
    /// The subscription returned is the existing one.
    /// </summary>
    public bool Created { get; set; }

    public SubscriptionDto? Subscription { get; set; }
}
