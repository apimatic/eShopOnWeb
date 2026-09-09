using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Saved-card flow: vault a card once and reuse it. A saved card belongs to the shopper who saved
/// it — one shopper never sees, uses or deletes another's. Full card details are never stored here.
/// </summary>
public interface ISavedCardService
{
    /// <summary>Vaults a card at PayPal and records a safe reference for <paramref name="ownerId"/>.</summary>
    Task<PaymentMethod> SaveCardAsync(string ownerId, CardDetails card, string? alias,
        CancellationToken cancellationToken = default);

    /// <summary>The shopper's saved cards.</summary>
    Task<IReadOnlyList<PaymentMethod>> GetCardsAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a saved card so it no longer appears among the shopper's cards and can no longer be
    /// used to pay. Scoped to <paramref name="ownerId"/>.
    /// </summary>
    Task DeleteCardAsync(string ownerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
