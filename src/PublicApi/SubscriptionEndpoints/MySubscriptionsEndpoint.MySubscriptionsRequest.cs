using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    /// <summary>
    /// The customer this call acts for. Taken from the authenticated principal, never from the body.
    /// </summary>
    [JsonIgnore]
    public string UserReference { get; set; } = string.Empty;
}
