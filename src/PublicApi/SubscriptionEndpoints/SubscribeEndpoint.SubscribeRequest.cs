namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    public string UserReference { get; init; } = string.Empty;
    public string ProductHandle { get; init; } = string.Empty;

    public SubscribeRequest()
    {
    }

    public SubscribeRequest(string userReference, string productHandle)
    {
        UserReference = userReference;
        ProductHandle = productHandle;
    }
}
