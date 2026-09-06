namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Body of POST /api/subscriptions. The shopper being subscribed always comes from the bearer
/// token, never from this payload.
/// </summary>
public class SubscribeRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by GET /api/subscription-plans.
    /// Optional only when the deployment configures <c>Maxio:DefaultPlanHandle</c>.
    /// </summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// Optional given name for the billing contact. Derived from the shopper's email when omitted,
    /// and ignored once their billing customer record exists.
    /// </summary>
    public string? FirstName { get; set; }

    /// <summary>
    /// Optional family name for the billing contact. Derived from the shopper's email when omitted,
    /// and ignored once their billing customer record exists.
    /// </summary>
    public string? LastName { get; set; }
}
