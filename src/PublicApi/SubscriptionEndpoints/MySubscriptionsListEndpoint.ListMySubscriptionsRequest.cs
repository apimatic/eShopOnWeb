using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsRequest : BaseRequest
{
    /// <summary>Resolved from the caller's JWT by the endpoint, not supplied by the client.</summary>
    [JsonIgnore]
    public string UserId { get; set; } = string.Empty;
}
