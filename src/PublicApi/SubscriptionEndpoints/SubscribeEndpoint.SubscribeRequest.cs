namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to subscribe the authenticated shopper to a plan. When <see cref="PlanHandle"/> is
/// omitted, the configured default plan (Maxio:DefaultPlanHandle) is used.
/// </summary>
public class SubscribeRequest : BaseRequest
{
    public string? PlanHandle { get; set; }
}
