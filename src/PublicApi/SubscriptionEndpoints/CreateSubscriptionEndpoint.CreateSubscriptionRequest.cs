namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    public string? ProductHandle { get; init; }

    public ApplicationCore.Models.MaxioBilling.ShopperIdentity Shopper { get; init; }
        = new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

    public CreateSubscriptionRequest(string? productHandle)
    {
        ProductHandle = productHandle;
    }
}