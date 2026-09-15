using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved (vaulted) cards. A saved card belongs to the shopper who saved it —
/// no shopper may see, use, or delete another's — and full card details are never stored.
/// </summary>
public interface ISavedPaymentMethodService
{
    /// <summary>Vaults a card for the shopper and returns the saved record (safe descriptors only).</summary>
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, string? label,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the shopper's own saved cards.</summary>
    Task<IReadOnlyCollection<SavedPaymentMethod>> GetForBuyerAsync(string buyerId,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a saved card so it no longer appears and can no longer be used to pay.</summary>
    Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
