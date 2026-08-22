using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentMethodService
{
    Task<SavedPaymentMethod> SaveAsync(string buyerId, CardPaymentDetails card, CancellationToken cancellationToken);

    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken);

    Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken);

    Task<SavedPaymentMethod?> GetOwnedAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken);
}
