using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Request for <c>GET /api/my-subscriptions</c>; identity is derived from the token.</summary>
public class MySubscriptionsRequest : BaseRequest
{
    [JsonIgnore]
    public SubscriberIdentity? Subscriber { get; set; }
}
