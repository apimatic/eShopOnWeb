using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedPaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISavedPaymentMethodService
{
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardPaymentSource card, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default);
    Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
