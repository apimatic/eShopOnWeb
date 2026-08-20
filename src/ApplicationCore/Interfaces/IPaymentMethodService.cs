using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentMethodService
{
    Task<SavedPaymentMethod> SaveCardAsync(
        string buyerId,
        CardPaymentSource card,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(
        string buyerId,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string buyerId,
        int paymentMethodId,
        CancellationToken cancellationToken = default);
}
