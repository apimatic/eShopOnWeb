using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Saves, lists and removes a shopper's vaulted cards. A card belongs to the shopper who saved it.</summary>
public interface ISavedCardService
{
    /// <summary>Vaults a card with PayPal and stores only its token and a safe descriptor.</summary>
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardPaymentDetails card, string? label, CancellationToken cancellationToken = default);

    /// <summary>The buyer's saved cards.</summary>
    Task<IReadOnlyList<SavedPaymentMethod>> GetForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Removes a saved card (from PayPal's vault and the app), so it can no longer be used to pay.</summary>
    Task RemoveAsync(string buyerId, int savedPaymentMethodId, CancellationToken cancellationToken = default);
}
