namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    public CreateSubscriptionRequest(string customerReference, string customerEmail, string planHandle)
    {
        CustomerReference = customerReference;
        CustomerEmail = customerEmail;
        PlanHandle = planHandle;
    }

    public string CustomerReference { get; }
    public string CustomerEmail { get; }
    public string PlanHandle { get; }
}
