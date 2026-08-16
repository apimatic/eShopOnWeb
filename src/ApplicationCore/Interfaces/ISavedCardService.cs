using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved (vaulted) cards. Every operation is scoped to the caller: one shopper
/// can never see, use or delete another's card. Full card details are vaulted with PayPal and never
/// stored in the application's own database.
/// </summary>
public interface ISavedCardService
{
    /// <summary>Vault a card for the shopper and store only its safe description plus the vault token.</summary>
    Task<SavedCard> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default);

    /// <summary>The caller's saved cards.</summary>
    Task<IReadOnlyList<SavedCard>> GetCardsAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove one of the caller's saved cards, deleting the PayPal vault token so it can no longer be
    /// used to pay. Returns false if no such card belongs to the caller.
    /// </summary>
    Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);

    /// <summary>Load one of the caller's saved cards, or null if it is not theirs / does not exist.</summary>
    Task<SavedCard?> GetOwnedCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
