using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentMethodService
{
    Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, string? alias, CancellationToken ct = default);

    Task<IReadOnlyList<PaymentMethod>> ListAsync(string buyerId, CancellationToken ct = default);

    Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct = default);
}
