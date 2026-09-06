using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsRequest : BaseRequest
{
    public ListMySubscriptionsRequest(SubscriberIdentity subscriber)
    {
        Subscriber = subscriber;
    }

    /// <summary>Resolved from the caller's access token by the route handler.</summary>
    [JsonIgnore]
    public SubscriberIdentity Subscriber { get; }
}
