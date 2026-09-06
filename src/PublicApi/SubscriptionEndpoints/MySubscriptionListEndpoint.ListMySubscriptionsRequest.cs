using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsRequest : BaseRequest
{
    /// <summary>The subscriber, taken from the bearer token.</summary>
    [JsonIgnore]
    public string? UserName { get; set; }
}
