using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISavedCardService
{
    Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct);

    Task<IReadOnlyList<PaymentMethod>> GetSavedCardsAsync(string buyerId, CancellationToken ct);

    /// <summary>Returns false if the payment method does not exist or does not belong to <paramref name="buyerId"/>.</summary>
    Task<bool> DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken ct);
}
