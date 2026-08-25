namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to (e.g. a handle listed by GET api/subscription-plans).
    /// </summary>
    public string ProductHandle { get; set; } = string.Empty;
}
