namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to (see GET api/subscription-plans).
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// The authenticated caller's username. Overwritten by the endpoint from the JWT before
    /// handling the request; any value supplied by the client here is ignored.
    /// </summary>
    public string Username { get; set; } = string.Empty;
}
