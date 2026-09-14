using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Resolves the authenticated caller (from the JWT) into a <see cref="ShopperIdentity"/> the billing
/// layer can use. Keeps ASP.NET Identity concerns out of <see cref="IMaxioBillingService"/>.
/// </summary>
public interface ICurrentShopperService
{
    /// <summary>
    /// Builds the current shopper's identity from the request principal and the identity store.
    /// Throws <see cref="MaxioBillingException"/> (401) when there is no authenticated user.
    /// </summary>
    Task<ShopperIdentity> GetCurrentShopperAsync(CancellationToken ct);
}
