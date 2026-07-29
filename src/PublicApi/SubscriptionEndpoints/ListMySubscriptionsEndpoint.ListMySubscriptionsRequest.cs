using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsRequest : BaseRequest
{
    /// <summary>
    /// The authenticated user's identity, populated from the JWT by the endpoint. Ignored on
    /// deserialization so it cannot be supplied by the caller.
    /// </summary>
    [JsonIgnore]
    public string? UserName { get; set; }
}
