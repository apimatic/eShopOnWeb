using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Manages a shopper's saved (vaulted) cards (Flow 2).</summary>
public interface IPaymentMethodService
{
    /// <summary>Vaults a card for the shopper and records a safe description of it.</summary>
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, PayPalCardDetails card, CancellationToken cancellationToken);

    /// <summary>The shopper's saved cards.</summary>
    Task<IReadOnlyList<SavedPaymentMethod>> GetCardsAsync(string buyerId, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the shopper's saved card (from PayPal's vault and our records). Returns false if the
    /// card does not exist or is not owned by the shopper.
    /// </summary>
    Task<bool> DeleteCardAsync(int paymentMethodId, string buyerId, CancellationToken cancellationToken);
}
