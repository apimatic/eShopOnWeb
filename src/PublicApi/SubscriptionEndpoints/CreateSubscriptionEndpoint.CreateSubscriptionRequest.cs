using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The Maxio product handle of the plan to subscribe to (e.g. from GET api/subscription-plans).
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// Populated from the JWT claims principal by the route delegate; not part of the request body.
    /// </summary>
    public string UserName { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    public SubscriptionDto Subscription { get; set; } = new();
}