using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved cards (Flow 2). Full card details are vaulted with PayPal and never stored in
/// the application's own database. Every operation is scoped to the caller: one shopper never sees, uses,
/// or deletes another's card.
/// </summary>
public interface ISavedCardService
{
    /// <summary>Vaults a card with PayPal and saves a safe reference for the shopper.</summary>
    Task<SavedCard> SaveCardAsync(string buyerId, PayPalCardDetails card, CancellationToken cancellationToken = default);

    /// <summary>The caller's saved cards.</summary>
    Task<IReadOnlyList<SavedCard>> GetCardsAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a saved card: deletes the PayPal vault token and the local record so it can no longer be seen
    /// or used to pay. Throws if the card is not the caller's.
    /// </summary>
    Task DeleteCardAsync(string buyerId, int savedCardId, CancellationToken cancellationToken = default);

    /// <summary>Loads a saved card that belongs to the caller, or null. Used by the pay flow.</summary>
    Task<SavedCard?> GetOwnedCardAsync(string buyerId, int savedCardId, CancellationToken cancellationToken = default);
}
