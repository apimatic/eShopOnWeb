using System.Security.Claims;
using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by <c>GET /api/subscription-plans</c>.
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// Supplied by the route from the validated bearer token. Internal, so it is neither bindable from the
    /// request body nor visible in the published schema — the caller cannot subscribe on someone else's
    /// behalf.
    /// </summary>
    internal ClaimsPrincipal? Caller { get; set; }

    /// <summary>Supplied by the route, not by the caller.</summary>
    internal CancellationToken CancellationToken { get; set; }
}
