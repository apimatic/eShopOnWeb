namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateShopperSubscriptionRequest : BaseRequest
{
    public string? ProductHandle { get; set; }
}
