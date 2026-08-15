using System.Threading;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request body for POST /api/subscriptions. The caller's identity is taken from the JWT, not the
/// body, so the only field a client supplies is the plan handle to subscribe to.
/// </summary>
public class SubscribeRequest : BaseRequest
{
    /// <summary>Stable handle of the plan to subscribe to (from GET /api/subscription-plans).</summary>
    public string PlanHandle { get; set; } = string.Empty;
}

/// <summary>
/// Internal, fully-resolved subscribe request passed to HandleAsync: the caller identity (from the
/// JWT) plus the requested plan handle and cancellation token.
/// </summary>
public class ResolvedSubscribeRequest : BaseRequest
{
    public ResolvedSubscribeRequest(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken)
    {
        Subscriber = subscriber;
        PlanHandle = planHandle;
        CancellationToken = cancellationToken;
    }

    public SubscriberIdentity Subscriber { get; }
    public string PlanHandle { get; }
    public CancellationToken CancellationToken { get; }
}
