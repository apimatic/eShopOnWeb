namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse()
    {
    }

    public SubscriptionDto Subscription { get; set; } = new SubscriptionDto();

    /// <summary>True when a new subscription was created; false when the shopper was already subscribed to this plan.</summary>
    public bool Created { get; set; }

    public string Message { get; set; } = string.Empty;
}
