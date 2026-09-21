namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeResponse : BaseResponse
{
    /// <summary>The resulting subscription (plan, price, state, next billing date).</summary>
    public CustomerSubscriptionDto? Subscription { get; set; }

    /// <summary>True when the subscription already existed (idempotent replay); false when newly created.</summary>
    public bool AlreadyExisted { get; set; }
}
