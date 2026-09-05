using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>The Maxio product handle of the plan to subscribe to, e.g. "eshop-pro".</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// The caller's identity. Never bound from client input - deliberately excluded from JSON
    /// (de)serialization and set server-side from the authenticated JWT after model binding.
    /// </summary>
    [JsonIgnore]
    public string Username { get; set; } = string.Empty;
}
