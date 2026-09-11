namespace Microsoft.eShopWeb.PublicApi.MaxioIntegration;

public class SubscriptionCreateRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}
