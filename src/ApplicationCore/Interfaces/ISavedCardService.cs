using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Drives Flow 2 — saving, listing, and removing a shopper's reusable cards.</summary>
public interface ISavedCardService
{
    /// <summary>Vaults a card for the shopper and stores only a safe descriptor plus the vault id.</summary>
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, PayPalCardDetails card, CancellationToken cancellationToken = default);

    /// <summary>The caller's own saved cards.</summary>
    Task<IReadOnlyList<SavedPaymentMethod>> ListCardsAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes one of the caller's saved cards (also deleting it at PayPal so it can no longer pay).
    /// Returns false if no such card belongs to the caller.
    /// </summary>
    Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
