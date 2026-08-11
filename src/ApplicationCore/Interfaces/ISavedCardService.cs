using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's vaulted cards. Full card details flow through to PayPal and are never
/// stored by the application; only a safe descriptor and the vault token are kept.
/// </summary>
public interface ISavedCardService
{
    /// <summary>Vault a card for the buyer and store its safe descriptor. Returns the saved card.</summary>
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, string? label,
        CancellationToken cancellationToken = default);

    /// <summary>The buyer's saved cards.</summary>
    Task<IReadOnlyList<SavedPaymentMethod>> GetCardsForBuyerAsync(string buyerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove a saved card owned by the buyer, deleting it from the vault too so it can no longer
    /// be used to pay.
    /// </summary>
    Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
