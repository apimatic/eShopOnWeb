namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// API handle of the plan to subscribe to (see GET /api/subscription-plans).
    /// </summary>
    public string ProductHandle { get; set; } = string.Empty;
}
