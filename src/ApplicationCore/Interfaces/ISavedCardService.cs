using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved cards: vaulting a card at PayPal and storing only its safe descriptor,
/// listing the shopper's own cards, and removing one (which also deletes it at PayPal so it can no longer
/// be used). All operations are scoped to the caller.
/// </summary>
public interface ISavedCardService
{
    /// <summary>Vaults a card for the shopper and stores a safe descriptor (never the card number).</summary>
    Task<SavedCard> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct);

    /// <summary>The shopper's saved cards.</summary>
    Task<IReadOnlyList<SavedCard>> GetCardsForBuyerAsync(string buyerId, CancellationToken ct);

    /// <summary>Loads one saved card, checking it belongs to the caller.</summary>
    Task<SavedCard?> GetCardForBuyerAsync(string buyerId, int paymentMethodId, CancellationToken ct);

    /// <summary>Removes a saved card (and deletes it at PayPal). Returns false if the caller has no such card.</summary>
    Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct);
}
