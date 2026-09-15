using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Manages a shopper's saved (vaulted) cards.</summary>
public interface ISavedCardService
{
    /// <summary>Vault a card for the shopper and store a safe descriptor of it.</summary>
    Task<SavedPaymentMethod> SaveCardAsync(
        string buyerId, CardPaymentDetails card, CancellationToken cancellationToken = default);

    /// <summary>The caller's saved cards.</summary>
    Task<IReadOnlyList<SavedPaymentMethod>> GetCardsForBuyerAsync(
        string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Remove a saved card (from PayPal's vault and this app), enforcing ownership.</summary>
    Task DeleteCardAsync(
        string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
