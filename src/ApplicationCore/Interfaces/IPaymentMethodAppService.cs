using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Saves, lists and removes a shopper's vaulted cards. All operations are scoped to the caller.</summary>
public interface IPaymentMethodAppService
{
    Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct = default);

    Task<IReadOnlyList<PaymentMethod>> ListAsync(string buyerId, CancellationToken ct = default);

    /// <summary>Removes a saved card. Returns false if it does not exist or does not belong to the caller.</summary>
    Task<bool> DeleteAsync(int paymentMethodId, string buyerId, CancellationToken ct = default);
}
