using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Save-a-card flow: vault a card once for a shopper and manage the saved list.</summary>
public interface ISavedCardService
{
    Task<Result<SavedPaymentMethod>> SaveCardAsync(
        string buyerId, CardDetails card, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<SavedPaymentMethod>>> ListForBuyerAsync(
        string buyerId, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(
        string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
