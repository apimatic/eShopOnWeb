namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to. Optional: when omitted the configured default plan is used,
    /// and a product family offering exactly one plan subscribes to that one.
    /// </summary>
    /// <remarks>
    /// There is deliberately no customer field — the shopper is always the bearer of the access token.
    /// </remarks>
    public string? PlanHandle { get; set; }
}
