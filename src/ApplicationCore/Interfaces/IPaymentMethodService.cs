using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved cards (Flow 2). Cards are vaulted in PayPal; this app stores only the
/// vault token and a safe descriptor. Every operation is scoped to the signed-in shopper.
/// </summary>
public interface IPaymentMethodService
{
    /// <summary>Saves (vaults) a card for the shopper and returns the stored, safely-described card.</summary>
    Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, string? alias, CancellationToken cancellationToken = default);

    /// <summary>Lists the shopper's saved cards.</summary>
    Task<IReadOnlyList<PaymentMethod>> GetCardsAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a saved card: deletes it from PayPal's vault and from this shopper's cards, so it can no
    /// longer be listed or used to pay. Returns false if the shopper has no such card.
    /// </summary>
    Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
