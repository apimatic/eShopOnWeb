using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Saves, lists and removes a shopper's reusable cards. Cards are vaulted with PayPal; this
/// app stores only the token reference and a safe descriptor. All operations are scoped to the
/// owning shopper (<c>buyerId</c>).
/// </summary>
public interface ISavedCardService
{
    /// <summary>Vaults a card with PayPal and records it as a saved card for the shopper.</summary>
    Task<SavedPaymentMethod> SaveCardAsync(
        string buyerId,
        CardDetails card,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the shopper's saved cards, newest first.</summary>
    Task<IReadOnlyList<SavedPaymentMethod>> GetCardsForBuyerAsync(
        string buyerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a saved card: deletes the PayPal vault token and the local record. Returns false
    /// if no such card belongs to the shopper. Afterwards the card can no longer be used to pay.
    /// </summary>
    Task<bool> DeleteCardAsync(
        string buyerId,
        int paymentMethodId,
        CancellationToken cancellationToken = default);
}
