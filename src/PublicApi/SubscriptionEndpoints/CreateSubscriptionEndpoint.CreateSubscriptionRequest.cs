namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The API handle of the plan to subscribe to (see GET /api/subscription-plans).
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// Set by the endpoint from the caller's JWT identity; any client-supplied value is discarded.
    /// </summary>
    public string Username { get; set; } = string.Empty;
}
