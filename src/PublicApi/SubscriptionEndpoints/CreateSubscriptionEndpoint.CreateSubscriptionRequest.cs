namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The handle of the plan to subscribe to (see GET api/subscription-plans).
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;

    // Populated server-side from the caller's JWT identity in CreateSubscriptionEndpoint.AddRoute,
    // after model binding - not intended to be supplied by the client.
    internal string UserId { get; set; } = string.Empty;
    internal string Email { get; set; } = string.Empty;
    internal string FirstName { get; set; } = string.Empty;
    internal string LastName { get; set; } = string.Empty;
}
