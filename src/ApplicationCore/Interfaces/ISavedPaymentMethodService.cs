using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISavedPaymentMethodService
{
    Task<PaymentMethod> SaveCardAsync(
        string buyerId,
        CardPaymentDetails card,
        string? alias,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken);

    Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken);
}
