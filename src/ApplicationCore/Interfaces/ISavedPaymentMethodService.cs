using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved cards, vaulting them with PayPal and storing only safe descriptors
/// locally. All operations are scoped to the supplied buyer id so one shopper never sees, uses or
/// deletes another's card.
/// </summary>
public interface ISavedPaymentMethodService
{
    /// <summary>Vaults a card for the buyer and returns the stored saved-card record.</summary>
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, PayPalCard card, string? alias, CancellationToken cancellationToken = default);

    /// <summary>Returns the buyer's saved cards.</summary>
    Task<IReadOnlyList<SavedPaymentMethod>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes one of the buyer's saved cards (from PayPal's vault and the local store). Returns false
    /// if no such card belongs to the buyer.
    /// </summary>
    Task<bool> DeleteAsync(int savedPaymentMethodId, string buyerId, CancellationToken cancellationToken = default);
}
