namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionCreationRequest : BaseRequest
{
    public string? ProductHandle { get; set; }
}
