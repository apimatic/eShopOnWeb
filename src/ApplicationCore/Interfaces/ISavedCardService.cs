using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Saves, lists and removes a shopper's cards. Cards are held in PayPal's vault; this app keeps
/// only a safe descriptor. Every operation is scoped to the owning shopper.
/// </summary>
public interface ISavedCardService
{
    Task<PaymentMethod> SaveCardAsync(string buyerId, PayPalCard card, CancellationToken ct = default);

    Task<IReadOnlyList<PaymentMethod>> ListCardsAsync(string buyerId, CancellationToken ct = default);

    /// <summary>Removes a saved card so it no longer appears for the shopper and can no longer pay.</summary>
    Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct = default);
}
