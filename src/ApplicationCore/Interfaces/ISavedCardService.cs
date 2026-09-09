using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved cards. Cards are vaulted with PayPal; only the vault token
/// and a safe description are kept locally. Every operation is scoped to the caller.
/// </summary>
public interface ISavedCardService
{
    Task<PaymentMethod> SaveCardAsync(string buyerId, PayPalCardDetails card, string? alias, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaymentMethod>> GetCardsAsync(string buyerId, CancellationToken cancellationToken = default);

    Task RemoveCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
