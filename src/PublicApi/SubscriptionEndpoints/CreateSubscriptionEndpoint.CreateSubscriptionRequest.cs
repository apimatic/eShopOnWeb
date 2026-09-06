namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by <c>GET /api/subscription-plans</c>.
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional caller-supplied key that makes this enrolment replayable. Sending the same key
    /// again returns the subscription the first request created instead of enrolling twice.
    /// </summary>
    public string? IdempotencyKey { get; set; }
}
