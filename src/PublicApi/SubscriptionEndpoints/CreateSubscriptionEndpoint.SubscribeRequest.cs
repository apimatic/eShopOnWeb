namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to enroll the authenticated caller in a plan. <see cref="PlanHandle"/> is the handle of
/// a plan returned by <c>GET /api/subscription-plans</c>.
/// </summary>
public class SubscribeRequest : BaseRequest
{
    public string? PlanHandle { get; set; }
}
