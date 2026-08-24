namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan (Maxio product) to subscribe to, e.g. as returned by GET /api/subscription-plans.
    /// </summary>
    public string ProductHandle { get; set; } = string.Empty;
}
