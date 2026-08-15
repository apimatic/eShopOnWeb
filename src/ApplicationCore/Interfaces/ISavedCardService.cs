using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved cards. The card lives in PayPal's vault; only a token and a safe
/// description are held locally. Every operation is scoped to the owning shopper.
/// </summary>
public interface ISavedCardService
{
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedPaymentMethod>> ListCardsAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Removes a saved card so it no longer appears for the shopper and can no longer be used to pay.</summary>
    Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
