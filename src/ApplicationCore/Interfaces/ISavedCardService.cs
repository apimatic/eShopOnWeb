using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Manages a shopper's saved (vaulted) cards. All operations are scoped to the owner.</summary>
public interface ISavedCardService
{
    /// <summary>Vaults a card for the shopper and returns the stored, safely-described saved card.</summary>
    Task<SavedCard> SaveCardAsync(string ownerId, CardDetails card, CancellationToken ct = default);

    /// <summary>The caller's saved cards.</summary>
    Task<IReadOnlyList<SavedCard>> GetCardsAsync(string ownerId, CancellationToken ct = default);

    /// <summary>Removes the caller's saved card so it can no longer be used to pay.</summary>
    Task DeleteCardAsync(string ownerId, int savedCardId, CancellationToken ct = default);
}
