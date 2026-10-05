using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to subscribe the authenticated shopper to a plan.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The handle of the plan to subscribe to (see GET api/subscription-plans).
    /// </summary>
    public string? PlanHandle { get; set; }
}

/// <summary>
/// The subscription created (or already held) by the shopper.
/// </summary>
public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse()
    {
    }

    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionDto? Subscription { get; set; }
}