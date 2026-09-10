using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved (vaulted) cards. All operations are shopper-scoped: a shopper can
/// only see, use, or delete their own cards. Full card details are never stored.
/// </summary>
public interface ISavedCardService
{
    /// <summary>Saves a card for the shopper and returns its safe description. Returns the payment-method id.</summary>
    Task<SavedCardView> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct);

    /// <summary>The caller's saved cards.</summary>
    Task<IReadOnlyList<SavedCardView>> GetCardsForBuyerAsync(string buyerId, CancellationToken ct);

    /// <summary>Removes a saved card so it no longer appears and can no longer be used to pay.</summary>
    Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct);
}
