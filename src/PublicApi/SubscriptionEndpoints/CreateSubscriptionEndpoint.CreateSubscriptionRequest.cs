namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    public string PlanHandle { get; set; } = string.Empty;

    public CreateSubscriptionRequest() { }

    public CreateSubscriptionRequest(string planHandle)
    {
        PlanHandle = planHandle;
    }
}
