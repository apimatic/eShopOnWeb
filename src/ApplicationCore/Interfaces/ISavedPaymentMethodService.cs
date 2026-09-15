using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISavedPaymentMethodService
{
    Task<SavedPaymentMethod> SaveCardAsync(
        string buyerId,
        CardPaymentDetails card,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(
        string buyerId,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string buyerId,
        int paymentMethodId,
        CancellationToken cancellationToken = default);
}
