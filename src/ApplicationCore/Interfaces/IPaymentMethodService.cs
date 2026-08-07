using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Saves, lists and removes a shopper's reusable cards. Cards live in PayPal's vault; only a safe
/// descriptor is kept locally. All operations are scoped to a shopper so one shopper can never see,
/// use or delete another's card.
/// </summary>
public interface IPaymentMethodService
{
    /// <summary>Vaults a card with PayPal and stores its safe descriptor for the shopper.</summary>
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default);

    /// <summary>All of the shopper's saved cards.</summary>
    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Removes a saved card. After this it no longer appears in the list and can no longer be used to pay.</summary>
    Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
