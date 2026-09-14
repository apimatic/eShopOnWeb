using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    /// <summary>The calling shopper, resolved server-side from the JWT.</summary>
    [JsonIgnore]
    public SubscriberIdentity? Subscriber { get; set; }
}
