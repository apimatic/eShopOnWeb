namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Body for <c>POST /api/subscriptions</c>. The subscriber is taken from the JWT, never from the
/// request, so only the plan is supplied here. When <see cref="PlanHandle"/> is omitted, the
/// configured default plan is used.
/// </summary>
public class SubscribeRequest : BaseRequest
{
    public string? PlanHandle { get; set; }
}
