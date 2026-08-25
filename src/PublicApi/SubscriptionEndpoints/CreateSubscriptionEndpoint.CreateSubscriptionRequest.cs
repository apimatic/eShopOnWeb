using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to, e.g. "eshop-pro".</summary>
    public string? PlanHandle { get; set; }

    // Populated from the authenticated caller's identity by the endpoint — never from the body.
    [JsonIgnore]
    public string? UserId { get; set; }

    [JsonIgnore]
    public string? Email { get; set; }
}
