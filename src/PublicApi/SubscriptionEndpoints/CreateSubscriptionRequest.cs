using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to subscribe the current user to a plan.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The handle of the Maxio product (plan) to subscribe to.
    /// </summary>
    public string ProductHandle { get; set; } = string.Empty;
}
