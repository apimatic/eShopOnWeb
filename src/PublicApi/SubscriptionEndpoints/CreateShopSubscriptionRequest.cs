namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateShopSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; set; }
}
