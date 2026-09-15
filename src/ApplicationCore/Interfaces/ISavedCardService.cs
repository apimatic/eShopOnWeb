using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISavedCardService
{
    Task<PaymentMethod> SaveCardAsync(string buyerIdentity, CardPaymentInput card, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaymentMethod>> ListAsync(string buyerIdentity, CancellationToken cancellationToken = default);

    Task DeleteAsync(string buyerIdentity, int paymentMethodId, CancellationToken cancellationToken = default);
}
