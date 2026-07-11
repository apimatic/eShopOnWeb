namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    public string BuyerId { get; init; } = string.Empty;
    public string ProductHandle { get; init; } = string.Empty;

    public SubscribeRequest(string buyerId, string productHandle)
    {
        BuyerId = buyerId;
        ProductHandle = productHandle;
    }
}
