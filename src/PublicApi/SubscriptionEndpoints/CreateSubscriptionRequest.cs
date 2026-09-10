namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to subscribe the authenticated caller to a plan. The plan is chosen by its
/// stable handle (numeric ids are not stable across catalog re-seeds).
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to, e.g. "eshop-pro".</summary>
    public string PlanHandle { get; set; } = string.Empty;
}
