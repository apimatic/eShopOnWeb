namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Payload used to subscribe the signed-in shopper to a plan.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>The handle of the plan to subscribe to (for example <c>eshop-pro</c>).</summary>
    public string PlanHandle { get; set; } = string.Empty;
}
