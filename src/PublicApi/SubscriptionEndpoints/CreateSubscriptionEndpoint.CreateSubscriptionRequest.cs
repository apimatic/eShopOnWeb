namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>API handle of the plan to subscribe to (see GET api/subscription-plans).</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>Set from the caller's JWT identity, not from the request body.</summary>
    public string Username { get; set; } = string.Empty;
}
