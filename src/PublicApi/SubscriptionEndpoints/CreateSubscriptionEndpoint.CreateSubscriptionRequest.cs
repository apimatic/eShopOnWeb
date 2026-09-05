using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>The handle of the plan to subscribe to, e.g. "eshop-pro".</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>Resolved from the caller's JWT by the endpoint, not supplied by the client.</summary>
    [JsonIgnore]
    public string UserId { get; set; } = string.Empty;

    /// <summary>Resolved from the caller's JWT by the endpoint, not supplied by the client.</summary>
    [JsonIgnore]
    public string UserEmail { get; set; } = string.Empty;
}
