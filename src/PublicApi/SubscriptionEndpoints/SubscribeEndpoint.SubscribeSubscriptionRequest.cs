using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to, e.g. <c>eshop-pro</c>.</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// Stable eShopOnWeb identifier for the caller, populated from the JWT on the server.
    /// Ignored on the wire so it can never be supplied (or spoofed) via the request body.
    /// </summary>
    [JsonIgnore]
    public string UserReference { get; set; } = string.Empty;

    /// <summary>Caller's email, resolved server-side from the authenticated identity. Not bound from the body.</summary>
    [JsonIgnore]
    public string Email { get; set; } = string.Empty;
}
