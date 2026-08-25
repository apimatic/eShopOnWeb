using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    // Populated from the authenticated caller's identity by the endpoint.
    [JsonIgnore]
    public string? UserId { get; set; }
}
