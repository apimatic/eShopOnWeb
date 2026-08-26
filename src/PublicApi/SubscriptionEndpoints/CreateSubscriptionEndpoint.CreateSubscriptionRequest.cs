using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to, e.g. "eshop-pro".</summary>
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>Populated from the JWT bearer token; never accepted from the request body.</summary>
    [JsonIgnore]
    public string? Username { get; set; }
}
