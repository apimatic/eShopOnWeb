using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansRequest : BaseRequest
{
    public ListSubscriptionPlansRequest(CancellationToken requestAborted)
    {
        RequestAborted = requestAborted;
    }

    public CancellationToken RequestAborted { get; }
}
