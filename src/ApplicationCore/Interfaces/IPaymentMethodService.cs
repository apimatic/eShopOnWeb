using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentMethodService
{
    Task<VaultedCardResult> SaveCardAsync(string buyerId, CardPaymentSource card, CancellationToken ct);
    Task<IReadOnlyList<PaymentMethod>> ListAsync(string buyerId, CancellationToken ct);
    Task DeleteAsync(string buyerId, string paymentMethodId, CancellationToken ct);
}
