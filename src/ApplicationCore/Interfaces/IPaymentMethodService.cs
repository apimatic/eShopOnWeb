using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payment;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentMethodService
{
    Task<SavedPaymentMethod> SaveAsync(
        string buyerId,
        CardPaymentInput card,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken);

    Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken);
}
