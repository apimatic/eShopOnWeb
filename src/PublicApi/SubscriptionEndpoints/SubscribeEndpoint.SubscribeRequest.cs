using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>The handle of the plan to enroll in. Falls back to the configured default plan.</summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// The customer this call acts for. Taken from the authenticated principal, never from the body.
    /// </summary>
    [JsonIgnore]
    public string UserReference { get; set; } = string.Empty;
}
