using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Saved-card flow: vault a card for the signed-in shopper, list their saved cards, and delete one.
/// Every operation is scoped to the caller; one shopper can never see, use or delete another's card.
/// </summary>
public interface ISavedCardService
{
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);

    /// <summary>Returns the saved card if it exists and belongs to the caller; otherwise null.</summary>
    Task<SavedPaymentMethod?> GetOwnedAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
