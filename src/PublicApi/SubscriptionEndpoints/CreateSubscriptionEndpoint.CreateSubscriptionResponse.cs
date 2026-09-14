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

    /// <summary>The subscription the shopper now holds.</summary>
    public SubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// True when this call enrolled the shopper (HTTP 201). False when they were already subscribed
    /// to the plan and the existing subscription was returned instead (HTTP 200).
    /// </summary>
    public bool Created { get; set; }

    /// <summary>The plan the subscription is for, as it stands in the billing catalog.</summary>
    public SubscriptionPlanDto? Plan { get; set; }
}
