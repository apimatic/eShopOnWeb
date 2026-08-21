using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved (vaulted) cards. Every method is scoped to the owning shopper — one
/// shopper can never see, use, or delete another's card.
/// </summary>
public interface ISavedCardService
{
    /// <summary>Vaults a card with PayPal and stores its safe descriptor for the shopper. Returns the saved-card id.</summary>
    Task<SavedPaymentMethod> SaveCardAsync(PayPalCardDetails card, string buyerId, CancellationToken cancellationToken = default);

    /// <summary>The caller's own saved cards.</summary>
    Task<IReadOnlyList<SavedPaymentMethod>> ListCardsAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Removes one of the caller's saved cards (from PayPal's vault and the store). Returns false if not found.</summary>
    Task<bool> DeleteCardAsync(int paymentMethodId, string buyerId, CancellationToken cancellationToken = default);
}
