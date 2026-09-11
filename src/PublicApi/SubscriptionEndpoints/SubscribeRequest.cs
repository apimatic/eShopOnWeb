namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest
{
    public string PlanHandle { get; set; } = "eshop-pro";
    public string Email { get; set; } = "";
}
