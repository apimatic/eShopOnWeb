namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Body of <c>POST /api/subscriptions</c>.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to (as returned by <c>GET /api/subscription-plans</c>),
    /// e.g. <c>eshop-pro</c>.
    /// </summary>
    public string? ProductHandle { get; set; }
}
