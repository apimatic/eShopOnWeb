using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Saves, lists and removes a shopper's reusable cards. All operations are owner-scoped.</summary>
public interface ISavedCardService
{
    /// <summary>Vaults the card with PayPal and stores only safe metadata for the shopper.</summary>
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct = default);

    /// <summary>The caller's saved cards.</summary>
    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken ct = default);

    /// <summary>
    /// Removes a saved card so it no longer appears for the caller and can no longer be used to pay.
    /// Returns false if the caller has no such card.
    /// </summary>
    Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct = default);
}
