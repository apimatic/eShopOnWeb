namespace Microsoft.eShopWeb.PublicApi.SubscriptionsEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    public string? ProductHandle { get; set; }
}
