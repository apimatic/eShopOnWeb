using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to subscribe the authenticated user to a plan.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}
