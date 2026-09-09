using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Saves, lists and removes a shopper's reusable cards. Full card details are never stored.</summary>
public interface IPaymentMethodService
{
    Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct = default);

    Task<IReadOnlyList<PaymentMethod>> ListAsync(string buyerId, CancellationToken ct = default);

    /// <summary>Remove a saved card. Returns false if the shopper has no such card.</summary>
    Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct = default);
}
