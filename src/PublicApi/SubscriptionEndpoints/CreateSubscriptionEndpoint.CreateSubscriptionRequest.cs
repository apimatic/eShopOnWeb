namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by GET /api/subscription-plans. Required:
    /// the API deliberately has no built-in default plan so the same build works against any catalog.
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;
}
