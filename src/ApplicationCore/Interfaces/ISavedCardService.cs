using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Saves, lists, and removes a shopper's vaulted cards. Every operation is scoped to the owning
/// shopper (<c>buyerId</c>): one shopper can never see, use, or delete another's card.
/// </summary>
public interface ISavedCardService
{
    /// <summary>Vault a card and persist a safe descriptor of it for the shopper.</summary>
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card,
        CancellationToken cancellationToken = default);

    /// <summary>The shopper's saved cards.</summary>
    Task<IReadOnlyList<SavedPaymentMethod>> ListCardsAsync(string buyerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove a saved card. After this succeeds the card no longer appears among the shopper's cards
    /// and can no longer be used to pay. <see cref="ResultStatus.NotFound"/> if it does not exist or
    /// is not owned by the shopper.
    /// </summary>
    Task<Result> DeleteCardAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken = default);
}
