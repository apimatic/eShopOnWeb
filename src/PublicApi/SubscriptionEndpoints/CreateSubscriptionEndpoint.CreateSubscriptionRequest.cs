namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to, e.g. one returned by GET /api/subscription-plans.</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// Populated server-side from the caller's JWT after model binding - never trust a
    /// client-supplied value for these two fields.
    /// </summary>
    public string UserReference { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
}
