using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>The handle of the plan to subscribe to, as returned by GET /api/subscription-plans.</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// Populated from the caller's JWT after model binding, never from client input -
    /// a shopper can only ever subscribe themselves.
    /// </summary>
    [JsonIgnore]
    public string Username { get; set; } = string.Empty;
}
