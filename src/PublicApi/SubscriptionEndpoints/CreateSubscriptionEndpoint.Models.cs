using System;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to enroll the authenticated user into a subscription plan
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The plan (Maxio product handle) to subscribe to, e.g. "eshop-pro"
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;
}

/// <summary>
/// Response confirming the user's subscription
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