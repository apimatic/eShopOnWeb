using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Saved cards for a shopper. Every operation is scoped to the signed-in shopper:
/// one shopper can never see, use, or delete another's card.
/// </summary>
public interface IPaymentMethodService
{
    /// <summary>Vaults a card with PayPal and saves a safe descriptor of it for the shopper.</summary>
    Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, string? alias,
        CancellationToken cancellationToken = default);

    /// <summary>The caller's saved cards.</summary>
    Task<IReadOnlyList<PaymentMethod>> GetCardsAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Removes a saved card. Returns false when the caller has no such card.</summary>
    Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
