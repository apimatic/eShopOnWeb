namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The plan (Maxio product handle) to subscribe to, e.g. "eshop-pro".
    /// Obtained from GET /api/subscription-plans.
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;
}
