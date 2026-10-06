using System;
using Microsoft.eShopWeb.PublicApi.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Response for a subscribe request
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

    /// <summary>
    /// True when the shopper already had a live subscription to this plan (the request was
    /// satisfied without creating a duplicate) instead of a brand-new enrollment.
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}
