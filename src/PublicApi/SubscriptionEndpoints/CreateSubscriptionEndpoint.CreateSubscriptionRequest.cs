namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by GET /api/subscription-plans. Required.
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional key that makes a retry of this exact request safe. It is stored as the
    /// subscription's reference in the billing system, so replaying the request returns the
    /// subscription the first attempt created rather than enrolling the shopper twice.
    /// </summary>
    public string? IdempotencyKey { get; set; }
}
