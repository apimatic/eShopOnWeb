using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan (Maxio product) to subscribe to.
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;

    // Identity is taken from the JWT, never from the request body.
    [JsonIgnore]
    public string UserId { get; set; } = string.Empty;

    [JsonIgnore]
    public string Email { get; set; } = string.Empty;
}
