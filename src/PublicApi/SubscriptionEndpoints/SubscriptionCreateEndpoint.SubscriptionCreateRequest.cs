using System;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscription;
using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to subscribe the authenticated user to a plan
/// </summary>
public class SubscriptionCreateRequest : BaseRequest
{
    /// <summary>
    /// The handle of the plan (Maxio product) to subscribe to, e.g. "eshop-pro"
    /// </summary>
    public string ProductHandle { get; set; } = string.Empty;
}

/// <summary>
/// Response confirming a subscription: plan, price, state and next billing date
/// </summary>
public class SubscriptionCreateResponse : BaseResponse
{
    public SubscriptionCreateResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionCreateResponse()
    {
    }

    /// <summary>
    /// True when the user was already subscribed and no new subscription was created
    /// </summary>
    public bool AlreadySubscribed { get; set; }

    public SubscriptionDto Subscription { get; set; } = new();
}