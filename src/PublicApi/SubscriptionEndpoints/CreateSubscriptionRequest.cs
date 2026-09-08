using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The plan handle (Maxio product API handle) to subscribe to, e.g. "eshop-pro".
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;
}
