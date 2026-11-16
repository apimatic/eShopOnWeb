using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISavedPaymentMethodService
{
    Task<SavedPaymentMethodView> SaveAsync(string buyerId, CardInput card,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedPaymentMethodView>> ListAsync(string buyerId,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken = default);
}
