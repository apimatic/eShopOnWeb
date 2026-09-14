using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the shopper behind the current bearer token.
/// <para>
/// Scoped, and taken as a route-handler parameter rather than an endpoint constructor dependency:
/// endpoint instances are built once at startup from the root provider, so anything scoped they
/// captured would be shared by every request.
/// </para>
/// </summary>
public interface ICurrentSubscriber
{
    /// <summary>The authenticated shopper, or null when the token carries no usable identity.</summary>
    Task<SubscriberIdentity?> GetAsync();
}
