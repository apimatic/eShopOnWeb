using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Request for POST /api/subscriptions.</summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>The API handle of the plan to subscribe to (e.g. "eshop-pro").</summary>
    public string PlanHandle { get; set; } = string.Empty;
}

/// <summary>Response for POST /api/subscriptions.</summary>
public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse()
    {
    }

    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    /// <summary>
    /// True when this call created a new subscription; false when the caller already held a live
    /// subscription to the plan and it was returned instead (idempotent double-click handling).
    /// </summary>
    public bool Created { get; set; }

    public SubscriptionDto Subscription { get; set; } = new();
}
