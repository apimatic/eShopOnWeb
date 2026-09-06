using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansRequest : BaseRequest
{
    public ListSubscriptionPlansRequest(CancellationToken cancellationToken)
    {
        CancellationToken = cancellationToken;
    }

    public CancellationToken CancellationToken { get; }
}
