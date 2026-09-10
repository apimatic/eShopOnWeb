using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Saved-card (vaulted card) operations, always scoped to the calling shopper.</summary>
public interface IPaymentMethodService
{
    Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaymentMethod>> GetForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Removes one of the caller's saved cards. Returns false if it does not exist for them.</summary>
    Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
