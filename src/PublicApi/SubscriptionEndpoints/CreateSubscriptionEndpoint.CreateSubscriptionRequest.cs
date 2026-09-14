namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The handle of the plan to subscribe to, e.g. "eshop-pro".
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// The caller's username, set server-side from the JWT after model binding. Any value
    /// supplied by the client in the request body is discarded.
    /// </summary>
    public string Username { get; set; } = string.Empty;
}
