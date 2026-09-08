using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the billing identity of the current JWT-authenticated caller from the token's
/// subject (the eShopOnWeb username) and the shared identity store.
/// </summary>
public interface ISubscriptionIdentityResolver
{
    Task<SubscriptionIdentity?> ResolveAsync();
}
