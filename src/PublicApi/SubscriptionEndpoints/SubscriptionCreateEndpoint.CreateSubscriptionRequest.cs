namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to subscribe the authenticated user to a plan.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Maxio product handle of the plan to subscribe to (e.g. "eshop-pro").
    /// </summary>
    public string ProductHandle { get; set; } = string.Empty;
}