namespace Microsoft.eShopWeb.PublicApi.MaxioEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    public string PlanHandle { get; set; } = string.Empty;
}
