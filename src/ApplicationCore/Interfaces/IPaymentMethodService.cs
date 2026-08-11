using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Saved-card flow: vault a card once for reuse, list a shopper's saved cards, and remove one.
/// Every method is scoped to the caller's own cards.
/// </summary>
public interface IPaymentMethodService
{
    Task<Result<PaymentMethod>> SaveCardAsync(string buyerId, CardDetails card,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<PaymentMethod>> GetCardsForBuyerAsync(string buyerId,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteCardAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken = default);
}
