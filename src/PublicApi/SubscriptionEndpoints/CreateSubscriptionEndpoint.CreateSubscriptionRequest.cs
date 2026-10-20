namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of a plan returned by <c>GET /api/subscription-plans</c> (e.g. <c>eshop-pro</c>).</summary>
    public string? PlanHandle { get; set; }
}
