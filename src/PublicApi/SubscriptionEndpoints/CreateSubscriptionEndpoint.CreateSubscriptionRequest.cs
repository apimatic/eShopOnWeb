using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The API handle of the plan (Maxio product) to subscribe to, e.g. from GET /api/subscription-plans.
    /// </summary>
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>
    /// Populated from the JWT bearer token; never read from the request body.
    /// </summary>
    [JsonIgnore]
    public string Username { get; set; } = string.Empty;
}
