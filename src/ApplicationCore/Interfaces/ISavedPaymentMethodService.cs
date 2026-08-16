using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved (vaulted) cards. A saved card belongs to the shopper who saved it;
/// callers only ever see or act on their own cards.
/// </summary>
public interface ISavedPaymentMethodService
{
    /// <summary>Vaults a card for the shopper and returns the stored (safe-descriptor) record.</summary>
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, PaymentCard card, CancellationToken cancellationToken = default);

    /// <summary>Lists the shopper's saved cards.</summary>
    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Removes one of the shopper's saved cards (from the vault and locally). Returns false if not found.</summary>
    Task<bool> DeleteAsync(int paymentMethodId, string buyerId, CancellationToken cancellationToken = default);
}
