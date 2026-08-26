namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to (see GET api/subscription-plans).
    /// </summary>
    public string? ProductHandle { get; set; }
}
