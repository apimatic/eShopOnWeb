namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeToPlanRequest : BaseRequest
{
    public string PlanHandle { get; set; } = string.Empty;
}
