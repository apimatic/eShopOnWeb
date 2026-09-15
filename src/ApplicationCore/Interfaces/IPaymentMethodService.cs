using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved cards. The card is vaulted at PayPal; this app keeps only the vault token
/// and a safe description. Every method is scoped to the owning shopper.
/// </summary>
public interface IPaymentMethodService
{
    /// <summary>Vaults a card for the shopper and returns the saved-card record.</summary>
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, PayPalCardDetails card, CancellationToken cancellationToken = default);

    /// <summary>The shopper's saved cards.</summary>
    Task<IReadOnlyList<SavedPaymentMethod>> GetForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Removes a saved card; afterwards it can no longer be listed or used to pay. Returns false if not found.</summary>
    Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
