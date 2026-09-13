namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansRequest : BaseRequest
{
    public string ProductFamilyHandle { get; set; } = string.Empty;
}
