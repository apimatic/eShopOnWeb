using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISavedPaymentMethodService
{
    Task<PaymentMethod> SavePaymentMethodAsync(string buyerId, PayPalCardDetails card, string? alias, CancellationToken ct = default);
    Task<IReadOnlyList<PaymentMethod>> ListPaymentMethodsAsync(string buyerId, CancellationToken ct = default);
    Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken ct = default);
}
