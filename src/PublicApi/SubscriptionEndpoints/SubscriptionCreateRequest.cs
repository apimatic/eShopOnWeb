using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionCreateRequest : BaseRequest
{
    /// <summary>
    /// Handle of the Maxio product (plan) to subscribe to, e.g. from
    /// GET /api/subscription-plans.
    /// </summary>
    public string ProductHandle { get; set; } = string.Empty;
}
