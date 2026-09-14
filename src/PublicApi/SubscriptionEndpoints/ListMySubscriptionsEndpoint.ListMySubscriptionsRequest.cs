using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsRequest : BaseRequest
{
    public string UserName { get; }
    public CancellationToken Ct { get; }

    public ListMySubscriptionsRequest(string userName, CancellationToken ct)
    {
        UserName = userName;
        Ct = ct;
    }
}
