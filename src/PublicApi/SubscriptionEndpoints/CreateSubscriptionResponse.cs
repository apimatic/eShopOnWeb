using System;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    /// <summary>The subscription the caller now has for the requested plan.</summary>
    public SubscriptionInfo Subscription { get; set; }

    /// <summary>
    /// True when this request created the subscription; false when an existing subscription for
    /// the same (user, plan) was returned instead.
    /// </summary>
    public bool IsNew { get; set; }
}
