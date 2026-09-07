namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionApiRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}
