using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Saves, lists and removes a shopper's vaulted cards. Every method is scoped to the owning buyer: one
/// shopper never sees, uses, or deletes another's card. Full card details are never stored.
/// </summary>
public interface ISavedCardService
{
    /// <summary>Vaults a card at PayPal and saves a safe descriptor for the buyer. Returns the saved card.</summary>
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct);

    /// <summary>The buyer's saved cards.</summary>
    Task<IReadOnlyList<SavedPaymentMethod>> GetCardsAsync(string buyerId, CancellationToken ct);

    /// <summary>
    /// Removes the buyer's saved card so it no longer appears and can no longer be used to pay.
    /// Returns false if no such card belongs to the buyer.
    /// </summary>
    Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct);
}
