using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Saves, lists and removes a shopper's vaulted cards. Every operation is shopper-scoped.</summary>
public interface IPaymentMethodService
{
    /// <summary>Vault a card for the shopper and store a safe description of it.</summary>
    Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card,
        CancellationToken cancellationToken = default);

    /// <summary>The shopper's saved cards.</summary>
    Task<IReadOnlyList<PaymentMethod>> ListAsync(string buyerId,
        CancellationToken cancellationToken = default);

    /// <summary>Remove a saved card so it no longer appears and can no longer be used to pay.</summary>
    Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
