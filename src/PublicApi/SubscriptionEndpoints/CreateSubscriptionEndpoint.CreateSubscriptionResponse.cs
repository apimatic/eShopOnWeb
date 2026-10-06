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

    public SubscriptionDto Subscription { get; set; } = null!;

    /// <summary>
    /// False when the request was a duplicate: the plan was already active for this user and the
    /// existing subscription was returned instead of enrolling a second one.
    /// </summary>
    public bool Created { get; set; }
}
