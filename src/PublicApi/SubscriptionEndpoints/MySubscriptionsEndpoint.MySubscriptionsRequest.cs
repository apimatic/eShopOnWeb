using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    /// <summary>Caller reference, populated server-side from the JWT; never bound from the request.</summary>
    [JsonIgnore]
    public string UserReference { get; set; } = string.Empty;
}
