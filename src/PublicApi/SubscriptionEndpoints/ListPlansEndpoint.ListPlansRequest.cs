using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListPlansRequest : BaseRequest
{
    public ListPlansRequest(CancellationToken cancellationToken)
    {
        CancellationToken = cancellationToken;
    }

    public CancellationToken CancellationToken { get; }
}
