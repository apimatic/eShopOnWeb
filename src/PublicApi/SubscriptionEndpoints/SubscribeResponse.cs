using Microsoft.eShopWeb.PublicApi.Maxio.Models;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeResponse : BaseResponse
{
    /// <summary>The shopper's subscription (newly created, or the existing one on an idempotent repeat).</summary>
    public SubscriptionDto? Subscription { get; set; }

    /// <summary>True when the shopper already had an active subscription to the plan and none was created.</summary>
    public bool AlreadySubscribed { get; set; }
}
