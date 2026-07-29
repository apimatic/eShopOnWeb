namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The API handle of the plan to subscribe to (a Maxio product handle, e.g. "eshop-pro"). Handles are
    /// stable across catalog re-seeds; numeric ids are not. Discover available handles via
    /// GET /api/subscription-plans.
    /// </summary>
    public string? PlanHandle { get; set; }
}
