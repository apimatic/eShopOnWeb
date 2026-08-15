using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Manages a shopper's saved (vaulted) cards. Every operation is scoped to the owning shopper.</summary>
public interface ISavedCardService
{
    /// <summary>Vault a card for the shopper and return the stored, safe-to-display record.</summary>
    Task<SavedCard> SaveCardAsync(string buyerId, CardInput card, CancellationToken cancellationToken = default);

    /// <summary>The shopper's own saved cards.</summary>
    Task<IReadOnlyList<SavedCard>> ListCardsAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Remove one of the shopper's own saved cards so it can no longer be charged.</summary>
    Task DeleteCardAsync(string buyerId, int savedCardId, CancellationToken cancellationToken = default);
}
