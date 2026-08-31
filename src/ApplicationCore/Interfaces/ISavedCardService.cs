using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISavedCardService
{
    /// <summary>Vault a card for the shopper and store only safe display data.</summary>
    Task<SavedCard> SaveAsync(string buyerId, GatewayCardDetails card, CancellationToken ct = default);

    /// <summary>The caller's own saved cards.</summary>
    Task<IReadOnlyList<SavedCard>> ListAsync(string buyerId, CancellationToken ct = default);

    /// <summary>Remove one of the caller's saved cards (also deleted at the provider).</summary>
    Task DeleteAsync(string buyerId, int savedCardId, CancellationToken ct = default);
}
