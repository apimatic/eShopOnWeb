namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The JSON body of POST /api/subscriptions. Only the plan is caller-supplied; the subscriber's identity
/// comes from the JWT, never from the body. <see cref="PlanHandle"/> is optional — when omitted the
/// configured default plan (or the first available plan) is used.
/// </summary>
public class SubscribeRequestBody
{
    public string? PlanHandle { get; set; }
}
