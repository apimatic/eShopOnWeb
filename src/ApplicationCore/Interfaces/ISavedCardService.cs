using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Saves, lists and removes a shopper's vaulted cards. Full card details go only to PayPal; this
/// app stores just the vault id and safe descriptors. Every operation is scoped to the owner.
/// </summary>
public interface ISavedCardService
{
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedPaymentMethod>> ListCardsAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Removes the caller's saved card. Returns false if no such card belongs to the caller.</summary>
    Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
