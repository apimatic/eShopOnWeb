using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Input for <see cref="SubscriptionPlanListEndpoint"/>. Built by the route delegate rather than
/// model-bound: listing plans takes no parameters, only the ambient request lifetime.
/// </summary>
public class ListSubscriptionPlansRequest : BaseRequest
{
    public ListSubscriptionPlansRequest(CancellationToken cancellationToken)
    {
        CancellationToken = cancellationToken;
    }

    public CancellationToken CancellationToken { get; }
}
