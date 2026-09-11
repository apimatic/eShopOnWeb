using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedPaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Saves, lists and removes a shopper's vaulted cards. A saved card belongs to the shopper
/// who saved it; the service scopes every operation to the caller.
/// </summary>
public interface IPaymentMethodService
{
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct = default);

    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken ct = default);

    /// <summary>Removes a saved card. Returns false when the card does not belong to the
    /// caller or does not exist.</summary>
    Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct = default);
}
