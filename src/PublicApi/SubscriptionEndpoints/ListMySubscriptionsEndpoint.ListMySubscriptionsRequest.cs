using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsRequest : BaseRequest
{
    // Identity is taken from the JWT, never from the request.
    [JsonIgnore]
    public string UserId { get; set; } = string.Empty;

    [JsonIgnore]
    public string Email { get; set; } = string.Empty;
}
