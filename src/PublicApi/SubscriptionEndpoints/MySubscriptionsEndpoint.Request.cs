using System.Threading;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    [JsonIgnore]
    public SubscriberIdentity? Subscriber { get; set; }

    [JsonIgnore]
    public CancellationToken CancellationToken { get; set; }
}
