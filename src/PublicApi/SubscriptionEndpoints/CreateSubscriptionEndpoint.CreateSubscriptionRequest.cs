namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to subscribe the calling user to a plan. The subscriber's identity is taken from the
/// JWT, never the body. <see cref="PlanHandle"/> is optional; when omitted the service falls back
/// to the first plan advertised by the configured product family.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    public string? PlanHandle { get; set; }
}
