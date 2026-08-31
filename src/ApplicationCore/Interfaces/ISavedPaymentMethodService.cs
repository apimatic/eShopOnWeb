using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Services;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's vaulted (saved) cards. Full card details go straight to the
/// payment processor's vault; only safe display data is stored locally.
/// </summary>
public interface ISavedPaymentMethodService
{
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a saved card both at the processor's vault and locally. Returns false when
    /// the card does not exist or does not belong to the shopper.
    /// </summary>
    Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
