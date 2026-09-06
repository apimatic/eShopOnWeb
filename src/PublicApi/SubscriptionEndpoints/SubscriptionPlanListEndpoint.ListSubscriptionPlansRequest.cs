using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansRequest : BaseRequest
{
    public ListSubscriptionPlansRequest(CancellationToken cancellationToken = default)
    {
        CancellationToken = cancellationToken;
    }

    /// <summary>Ties outbound billing calls to the lifetime of the HTTP request.</summary>
    public CancellationToken CancellationToken { get; }
}
