namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The Maxio product handle to subscribe to (e.g. from <c>GET /api/subscription-plans</c>). When
    /// omitted, the lowest-priced available plan is used.
    /// </summary>
    public string? PlanHandle { get; set; }
}
