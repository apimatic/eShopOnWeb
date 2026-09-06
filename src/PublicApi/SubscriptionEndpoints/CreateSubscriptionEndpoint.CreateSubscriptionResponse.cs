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

    /// <summary>The subscription now backing the caller's enrollment.</summary>
    public CustomerSubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// True when the caller was already enrolled on this plan and nothing new was created. The request
    /// is answered 200 rather than 201 in that case.
    /// </summary>
    public bool AlreadySubscribed { get; set; }

    /// <summary>True when a billing customer record had to be created for the caller as part of this request.</summary>
    public bool CustomerCreated { get; set; }
}
