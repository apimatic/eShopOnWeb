using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetMySubscriptionsRequest : BaseRequest
{
    /// <summary>
    /// The subscriber's identity, taken from the authenticated token — never from the request body.
    /// </summary>
    [JsonIgnore]
    public string UserName { get; set; }
}
