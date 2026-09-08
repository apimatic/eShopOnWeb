using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    public CreateSubscriptionRequest(string productHandle)
    {
        ProductHandle = productHandle;
    }

    public CreateSubscriptionRequest() { }

    /// <summary>Handle of the plan to subscribe to (see GET api/subscription-plans).</summary>
    public string ProductHandle { get; set; } = string.Empty;
}
