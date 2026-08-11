using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISavedCardService
{
    /// <summary>Vaults a card in PayPal and records a safe reference to it for the buyer.</summary>
    Task<SavedCard> SaveCardAsync(string buyerId, CardDetails card, string? label,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the buyer's saved cards (never full card details).</summary>
    Task<IReadOnlyList<SavedCard>> ListCardsAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a saved card owned by the buyer, deleting it from PayPal's vault so it can no longer
    /// be used to pay. Returns false when the card does not exist for this buyer.
    /// </summary>
    Task<bool> RemoveCardAsync(string buyerId, int savedCardId, CancellationToken cancellationToken = default);
}
