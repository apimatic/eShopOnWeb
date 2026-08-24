using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsRequest : BaseRequest
{
    /// <summary>
    /// Populated from the JWT bearer token; never read from the request.
    /// </summary>
    [JsonIgnore]
    public string Username { get; set; } = string.Empty;
}
