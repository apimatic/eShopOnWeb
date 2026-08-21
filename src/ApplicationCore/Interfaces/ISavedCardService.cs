using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved (vaulted) cards. A saved card belongs only to the shopper who saved
/// it; full card details are never stored in this app.
/// </summary>
public interface ISavedCardService
{
    /// <summary>Vaults a card for the shopper and returns the saved payment method.</summary>
    Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, string? alias, CancellationToken cancellationToken = default);

    /// <summary>The shopper's own saved cards.</summary>
    Task<IReadOnlyList<PaymentMethod>> GetCardsAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Removes one of the shopper's saved cards so it can no longer be used to pay.</summary>
    Task<Result> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
