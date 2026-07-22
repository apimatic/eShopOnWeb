using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    public MySubscriptionsRequest(string userReference, CancellationToken cancellationToken)
    {
        UserReference = userReference;
        CancellationToken = cancellationToken;
    }

    public string UserReference { get; }
    public CancellationToken CancellationToken { get; }
}
