using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Manages a shopper's saved (vaulted) cards.</summary>
public interface IPaymentMethodService
{
    /// <summary>Vault a card with PayPal and save a safe reference to it for the shopper.</summary>
    Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, string? alias, CancellationToken cancellationToken = default);

    /// <summary>The shopper's saved cards.</summary>
    Task<IReadOnlyList<PaymentMethod>> GetForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove a saved card: delete it from PayPal's vault and from this app so it can no longer be
    /// used to pay. Throws <see cref="Exceptions.EntityNotFoundException"/> if it is not the caller's.
    /// </summary>
    Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
