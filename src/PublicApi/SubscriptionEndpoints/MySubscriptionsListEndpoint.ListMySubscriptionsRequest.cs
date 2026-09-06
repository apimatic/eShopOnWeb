using System.Security.Claims;
using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsRequest : BaseRequest
{
    /// <summary>Supplied by the route from the validated bearer token.</summary>
    internal ClaimsPrincipal? Caller { get; set; }

    /// <summary>Supplied by the route, not by the caller.</summary>
    internal CancellationToken CancellationToken { get; set; }
}
