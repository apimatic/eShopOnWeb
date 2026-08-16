namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request body for POST /api/subscriptions. The shopper's identity is taken from the JWT,
/// never from the body.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>The stable handle of the plan to subscribe to (from GET /api/subscription-plans).</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>Optional product price point handle when a plan exposes more than one price point.</summary>
    public string? PricePointHandle { get; set; }
}
