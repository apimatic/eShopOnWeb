using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISavedPaymentMethodService
{
    Task<SavedPaymentMethod> SaveAsync(string buyerId, CardInput card, CancellationToken cancellationToken);
    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId);
    Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken);
    Task<SavedPaymentMethod> GetOwnedAsync(string buyerId, int paymentMethodId);
}
