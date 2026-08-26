using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to (see GET api/subscription-plans).</summary>
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>Set from the JWT by the endpoint; never read from the request body.</summary>
    [JsonIgnore]
    public string Username { get; set; } = string.Empty;
}
