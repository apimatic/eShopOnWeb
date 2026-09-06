using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsRequest : BaseRequest
{
    public ListMySubscriptionsRequest(string userKey, CancellationToken cancellationToken)
    {
        UserKey = userKey;
        CancellationToken = cancellationToken;
    }

    /// <summary>Stable key of the authenticated shopper, taken from the bearer token.</summary>
    public string UserKey { get; }

    public CancellationToken CancellationToken { get; }
}
