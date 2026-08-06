using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved cards. Raw card details are vaulted with PayPal and never stored
/// locally; a saved card always belongs to the shopper who saved it.
/// </summary>
public interface IPaymentMethodService
{
    /// <summary>Vaults a card for the shopper and returns the saved payment method (with its id).</summary>
    Task<PaymentMethod> SaveCardAsync(string buyerId, CardPaymentDetails card, CancellationToken cancellationToken = default);

    /// <summary>Lists the shopper's saved cards.</summary>
    Task<IReadOnlyCollection<PaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes one of the shopper's saved cards (from PayPal's vault and locally).
    /// Returns false if the shopper has no such saved card.
    /// </summary>
    Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
