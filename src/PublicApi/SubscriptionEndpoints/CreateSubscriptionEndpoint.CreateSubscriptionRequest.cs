using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>The Maxio plan (product) handle to subscribe to, e.g. "eshop-pro".</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// Populated by the endpoint from the authenticated caller's identity before HandleAsync
    /// runs - never bound from the request body.
    /// </summary>
    public MaxioCustomerProfile Buyer { get; set; } = null!;
}
