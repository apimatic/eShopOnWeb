using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }
    public CreateSubscriptionResponse() { }

    public SubscriptionDto? Subscription { get; set; }

    /// <summary>True when this call created a new subscription; false when an existing one was returned.</summary>
    public bool Created { get; set; }
}
