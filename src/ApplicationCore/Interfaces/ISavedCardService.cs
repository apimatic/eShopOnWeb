using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISavedCardService
{
    Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct = default);

    Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken ct = default);
}
