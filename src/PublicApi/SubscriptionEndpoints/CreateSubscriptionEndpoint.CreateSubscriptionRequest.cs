using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to subscribe the authenticated user to a plan
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The handle of the plan (Maxio product) to subscribe to, e.g. from GET api/subscription-plans.
    /// </summary>
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>
    /// Populated from the JWT identity by the endpoint; not supplied by callers.
    /// </summary>
    public string Username { get; set; } = string.Empty;
}

/// <summary>
/// Response confirming the subscription: plan, price, state and next billing date
/// </summary>
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
