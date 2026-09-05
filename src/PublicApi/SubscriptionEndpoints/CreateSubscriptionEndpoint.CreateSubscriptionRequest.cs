using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The Maxio plan handle to subscribe to (e.g. "eshop-pro").
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// Set by the endpoint from the caller's JWT identity — never bound from the request body,
    /// so a caller cannot subscribe on behalf of another user.
    /// </summary>
    [JsonIgnore]
    public string Username { get; set; } = string.Empty;
}
