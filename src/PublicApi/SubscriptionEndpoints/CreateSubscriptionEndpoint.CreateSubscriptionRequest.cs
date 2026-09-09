namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to (e.g. "eshop-pro").
    /// When omitted, the default plan of the configured product family is used.
    /// </summary>
    public string? PlanHandle { get; set; }
}
