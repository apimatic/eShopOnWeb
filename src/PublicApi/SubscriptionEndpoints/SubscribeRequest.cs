namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Body for POST /api/subscriptions. <see cref="PlanHandle"/> is a plan handle from
/// GET /api/subscription-plans (e.g. <c>eshop-pro</c>).
/// </summary>
public class SubscribeRequest : BaseRequest
{
    public string? PlanHandle { get; set; }
}
