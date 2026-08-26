using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsRequest : BaseRequest
{
    /// <summary>Set from the JWT by the endpoint; never read from the request.</summary>
    [JsonIgnore]
    public string Username { get; set; } = string.Empty;
}
