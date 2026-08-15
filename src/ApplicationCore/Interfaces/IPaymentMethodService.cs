using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved (vaulted) cards. Full card details are never stored — only PayPal's
/// vault token and a safe description. Every operation is scoped to the calling shopper.
/// </summary>
public interface IPaymentMethodService
{
    /// <summary>Vault a card for the buyer and record a safe descriptor; returns the saved card.</summary>
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, PayPalCardInput card, CancellationToken cancellationToken = default);

    /// <summary>The buyer's own saved cards.</summary>
    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Remove the buyer's saved card (from the vault and locally) so it can no longer pay.</summary>
    Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
