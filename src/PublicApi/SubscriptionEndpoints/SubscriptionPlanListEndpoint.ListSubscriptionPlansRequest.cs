using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansRequest : BaseRequest
{
    /// <summary>Supplied by the route, not by the caller.</summary>
    internal CancellationToken CancellationToken { get; set; }
}
