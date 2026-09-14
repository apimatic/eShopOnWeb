using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>The handle of the plan to subscribe to (from the subscription catalog).</summary>
    public string ProductHandle { get; set; } = string.Empty;
}
