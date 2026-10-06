using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request for POST api/subscriptions.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to (see GET api/subscription-plans).
    /// </summary>
    public string? PlanHandle { get; set; }
}

/// <summary>
/// Response for POST api/subscriptions.
/// </summary>
public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    public SubscriptionDto? Subscription { get; set; }
}