using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISavedCardService
{
    Task<SavedCard> SaveAsync(string buyerId, CardPaymentRequest card, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedCard>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);

    Task<SavedCard> GetOwnedAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
