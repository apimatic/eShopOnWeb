using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>The handle of one of the plans from <c>GET /api/subscription-plans</c>.</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>The caller, taken from the JWT; never from the request body.</summary>
    [JsonIgnore]
    public SubscriberIdentity? Subscriber { get; set; }
}
