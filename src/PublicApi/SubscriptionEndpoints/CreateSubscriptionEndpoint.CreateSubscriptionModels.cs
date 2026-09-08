using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The handle of the plan to subscribe to (e.g. "eshop-pro"). Use the stable handle,
    /// not the numeric id: Maxio reassigns numeric ids when the demo catalog is re-seeded.
    /// </summary>
    public string? PlanHandle { get; set; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    public SubscriptionDto? Subscription { get; set; }

    /// <summary>True when this call created the subscription; false when an existing subscription was returned.</summary>
    public bool Created { get; set; }
}
