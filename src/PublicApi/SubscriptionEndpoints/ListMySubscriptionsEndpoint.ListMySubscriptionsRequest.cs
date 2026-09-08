using System.ComponentModel;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsRequest : BaseRequest
{
    /// <summary>The authenticated user's identity. Always set server-side from the JWT.</summary>
    [ReadOnly(true)]
    public string? UserName { get; set; }
}
