using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved cards. Cards are vaulted with PayPal; this app stores only a token
/// and a safe description. Every operation is scoped to the owning shopper.
/// </summary>
public interface ISavedCardService
{
    Task<SavedCard> SaveCardAsync(string buyerId, PayPalCard card, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedCard>> ListCardsAsync(string buyerId, CancellationToken cancellationToken = default);

    Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
