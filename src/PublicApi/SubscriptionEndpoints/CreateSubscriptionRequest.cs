namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Creates (or idempotently returns) a subscription for the authenticated shopper.
/// </summary>
public class CreateSubscriptionRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to (e.g. "eshop-pro"). When omitted, the
    /// configured default plan (Maxio:DefaultPlanHandle) is used.
    /// </summary>
    public string? ProductHandle { get; set; }
}
