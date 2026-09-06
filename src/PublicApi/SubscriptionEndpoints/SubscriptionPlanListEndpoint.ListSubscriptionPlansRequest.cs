using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Internal request for <see cref="SubscriptionPlanListEndpoint"/>. Not bound from the wire: the
/// endpoint takes no input beyond the caller's token.
/// </summary>
public class ListSubscriptionPlansRequest : BaseRequest
{
    public ListSubscriptionPlansRequest(CancellationToken cancellationToken)
    {
        CancellationToken = cancellationToken;
    }

    /// <summary>Carried on the request so a client that disconnects stops work against the billing system.</summary>
    public CancellationToken CancellationToken { get; }
}
