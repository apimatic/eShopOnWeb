namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>The handle of a plan returned by <c>GET /api/subscription-plans</c>.</summary>
    public string PlanHandle { get; set; } = string.Empty;
}
