using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISavedPaymentMethodService
{
    Task<SavedPaymentMethod> SaveAsync(string buyerId, CardPaymentInput card, CancellationToken ct);
    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken ct);
    Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct);
    Task<SavedPaymentMethod?> GetOwnedAsync(string buyerId, int paymentMethodId, CancellationToken ct);
}
