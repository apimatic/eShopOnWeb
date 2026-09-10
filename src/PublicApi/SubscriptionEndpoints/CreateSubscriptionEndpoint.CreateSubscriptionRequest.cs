namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to subscribe the authenticated caller to a plan. The caller's identity is taken from the JWT,
/// not the body. <see cref="PlanHandle"/> is optional — when omitted, the configured default plan is used.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    public string? PlanHandle { get; set; }
}
