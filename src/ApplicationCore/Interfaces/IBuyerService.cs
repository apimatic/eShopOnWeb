using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Resolves the <see cref="Buyer"/> aggregate that owns a shopper's saved cards, keyed by the
/// authenticated identity (user name / email).
/// </summary>
public interface IBuyerService
{
    /// <summary>Gets the buyer for this identity (with saved cards loaded), creating it if needed.</summary>
    Task<Buyer> GetOrCreateBuyerAsync(string identity, CancellationToken cancellationToken = default);

    /// <summary>Gets the buyer for this identity (with saved cards loaded), or null if none exists.</summary>
    Task<Buyer?> GetBuyerAsync(string identity, CancellationToken cancellationToken = default);
}
