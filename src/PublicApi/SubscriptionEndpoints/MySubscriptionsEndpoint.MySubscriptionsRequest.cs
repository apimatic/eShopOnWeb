using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    /// <summary>Resolved from the bearer token; never supplied by the caller.</summary>
    [JsonIgnore]
    public string? UserName { get; set; }
}
