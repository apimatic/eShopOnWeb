using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Plan (product handle) to subscribe to. When omitted, the configured default plan is used.
    /// </summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// The subscriber's stable reference — set by the endpoint from the authenticated token,
    /// never from the request body.
    /// </summary>
    [JsonIgnore]
    public string? SubscriberReference { get; set; }

    /// <summary>The subscriber's email — set by the endpoint from the authenticated token.</summary>
    [JsonIgnore]
    public string? SubscriberEmail { get; set; }
}
