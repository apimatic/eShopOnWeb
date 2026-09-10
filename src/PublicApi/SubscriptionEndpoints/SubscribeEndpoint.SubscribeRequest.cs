namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Body of POST /api/subscriptions. The subscribing shopper is taken from the authenticated
/// token, never from this payload; only the plan choice and optional display name are accepted.
/// </summary>
public class SubscribeRequest : BaseRequest
{
    /// <summary>Stable handle of the plan to subscribe to (e.g. from GET /api/subscription-plans).</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>Optional given name used when the billing customer is first created.</summary>
    public string? FirstName { get; set; }

    /// <summary>Optional family name used when the billing customer is first created.</summary>
    public string? LastName { get; set; }
}
