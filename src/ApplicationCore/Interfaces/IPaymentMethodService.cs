using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Manages a shopper's saved cards (PayPal vault tokens).</summary>
public interface IPaymentMethodService
{
    Task<PaymentMethod> SavePaymentMethodAsync(string buyerId, CardDetails card, string? alias);

    Task<IReadOnlyList<PaymentMethod>> GetPaymentMethodsAsync(string buyerId);

    /// <summary>Returns false if the payment method doesn't exist or isn't owned by <paramref name="buyerId"/>.</summary>
    Task<bool> DeletePaymentMethodAsync(string buyerId, int paymentMethodId);
}
