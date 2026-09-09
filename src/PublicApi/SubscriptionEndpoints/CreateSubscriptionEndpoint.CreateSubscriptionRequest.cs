using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>The stable handle of the plan to subscribe to, e.g. <c>eshop-pro</c>.</summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// The subscriber's email. Set server-side from the authenticated token — any value sent by
    /// the client is ignored, so callers can only ever subscribe on their own behalf.
    /// </summary>
    [JsonIgnore]
    public string? SubscriberEmail { get; set; }
}
