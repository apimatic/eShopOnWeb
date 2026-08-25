using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Owns saving, listing and removing a shopper's vaulted cards.</summary>
public interface IPaymentMethodService
{
    Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct);

    Task<IReadOnlyList<PaymentMethod>> ListForBuyerAsync(string buyerId, CancellationToken ct);

    /// <summary>Null when the payment method does not exist or does not belong to <paramref name="buyerId"/>.</summary>
    Task<PaymentMethod?> GetForBuyerAsync(string buyerId, int paymentMethodId, CancellationToken ct);

    /// <summary>False when the payment method does not exist or does not belong to <paramref name="buyerId"/>.</summary>
    Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct);
}
