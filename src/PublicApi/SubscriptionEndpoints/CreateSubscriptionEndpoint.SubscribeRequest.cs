namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Body of <c>POST /api/subscriptions</c>. The shopper is taken from the bearer token; this carries only
/// what the shopper is choosing.
/// </summary>
public class SubscribeRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by <c>GET /api/subscription-plans</c>. Required.
    /// </summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// Optional given name used only when the shopper's billing customer record is created for the first
    /// time. When omitted a name is derived from the shopper's e-mail address.
    /// </summary>
    public string? FirstName { get; set; }

    /// <summary>Optional family name, used under the same conditions as <see cref="FirstName"/>.</summary>
    public string? LastName { get; set; }
}
