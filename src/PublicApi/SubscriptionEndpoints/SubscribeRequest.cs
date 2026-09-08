using System;
using System.Security.Claims;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to (for example the handle shown by
    /// GET api/subscription-plans).
    /// </summary>
    public string ProductHandle { get; set; } = string.Empty;
}
