using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Saves, lists and removes a shopper's cards (Flow 2). Scoped to the caller's own cards.</summary>
public interface ISavedCardService
{
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct);

    Task<IReadOnlyList<SavedPaymentMethod>> GetCardsForBuyerAsync(string buyerId, CancellationToken ct);

    Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct);
}

/// <summary>Supplies the configured payment currency to the application layer.</summary>
public interface IPaymentCurrencyProvider
{
    string CurrencyCode { get; }
}
