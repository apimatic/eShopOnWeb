using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>API handle of the plan to subscribe to, e.g. "eshop-pro".</summary>
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>Populated from the JWT principal, not from the request body.</summary>
    [JsonIgnore]
    public string Username { get; set; } = string.Empty;
}
