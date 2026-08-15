using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to (e.g. <c>eshop-pro</c>). Optional: when omitted the server
    /// falls back to its configured default plan, or the flagship (highest-priced) plan in the family.
    /// </summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// The shopper's identity. Populated server-side from the JWT — any value sent by the client is
    /// ignored — so it is never read from the request body.
    /// </summary>
    [JsonIgnore]
    public string Username { get; set; } = string.Empty;
}
