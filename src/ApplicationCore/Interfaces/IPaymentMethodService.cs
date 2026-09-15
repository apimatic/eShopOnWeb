using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payment;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentMethodService
{
    Task<PaymentMethod> SaveCardAsync(string buyerIdentity, CardPaymentDetails card, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaymentMethod>> ListAsync(string buyerIdentity, CancellationToken cancellationToken = default);

    Task DeleteAsync(string buyerIdentity, int paymentMethodId, CancellationToken cancellationToken = default);
}
