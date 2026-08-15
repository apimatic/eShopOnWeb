using System.Text.Json.Serialization;
using System.Threading;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetMySubscriptionsRequest : BaseRequest
{
    [JsonIgnore]
    public SubscriberIdentity? Subscriber { get; set; }

    [JsonIgnore]
    public CancellationToken CancellationToken { get; set; }
}
