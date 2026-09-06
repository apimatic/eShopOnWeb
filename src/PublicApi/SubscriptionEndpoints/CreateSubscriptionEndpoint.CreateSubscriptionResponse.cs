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

    /// <summary>The shopper's subscription, whether it was just created or already existed.</summary>
    public SubscriptionDto Subscription { get; set; } = new();

    /// <summary>
    /// True when the request was a no-op because an equivalent subscription was already on file. The
    /// endpoint answers <c>200 OK</c> in that case and <c>201 Created</c> when it really did enroll.
    /// </summary>
    public bool AlreadyExisted { get; set; }
}
