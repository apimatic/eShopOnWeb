namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The Maxio product handle of the plan to subscribe to (see GET api/subscription-plans).
    /// </summary>
    public string ProductHandle { get; set; } = string.Empty;
}
