using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;
using Microsoft.eShopWeb.ApplicationCore.Services;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Saved-card flow for a shopper. A saved card belongs to the shopper who saved it: one shopper
/// never sees, uses, or deletes another's. Full card details are never stored by this app.
/// </summary>
public interface ISavedCardService
{
    Task<SavedCardView> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedCardView>> GetCardsAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Removes a saved card. Afterwards it no longer appears for the caller and can no longer pay.</summary>
    Task RemoveCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
