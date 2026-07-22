namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>Handle of the plan to enroll in.</summary>
    public string PlanHandle { get; set; }

    /// <summary>
    /// Set from the bearer token's identity, never from the request body.
    /// </summary>
    internal string? UserReference { get; set; }
}
