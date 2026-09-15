using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved cards. Every operation is scoped to the caller's <c>buyerId</c>:
/// one shopper can never see, use or delete another's saved card.
/// </summary>
public interface IPaymentMethodService
{
    /// <summary>Vaults a card at PayPal and stores a safe reference for the shopper.</summary>
    Task<SavedCard> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default);

    /// <summary>The caller's saved cards.</summary>
    Task<IReadOnlyList<SavedCard>> ListCardsAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes one of the caller's saved cards. After this it no longer appears among the
    /// caller's saved cards and can no longer be used to pay. Returns false if not found for the caller.
    /// </summary>
    Task<bool> DeleteCardAsync(string buyerId, int savedCardId, CancellationToken cancellationToken = default);
}
