namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The stable handle of the plan to subscribe to (e.g. <c>eshop-pro</c>). Obtain valid
    /// handles from <c>GET /api/subscription-plans</c>.
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;
}
