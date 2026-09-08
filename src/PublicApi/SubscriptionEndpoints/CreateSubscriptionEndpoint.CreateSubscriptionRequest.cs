using System.ComponentModel;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to (e.g. "eshop-pro"). Handles are stable across re-seeds; numeric
    /// ids are not. Required.
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// The authenticated user's identity. Always set server-side from the JWT; any value sent by the
    /// client is ignored.
    /// </summary>
    [ReadOnly(true)]
    public string? UserName { get; set; }
}
