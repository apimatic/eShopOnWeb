using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISavedPaymentMethodService
{
    Task<PaymentMethod> SaveCardAsync(string buyerId, CardPaymentInput card, string? alias, CancellationToken ct);
    Task<IReadOnlyList<PaymentMethod>> ListAsync(string buyerId, CancellationToken ct);
    Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct);
}
