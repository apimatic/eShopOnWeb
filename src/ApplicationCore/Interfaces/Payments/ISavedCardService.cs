using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>Manages a shopper's saved (vaulted) cards. All operations are scoped to the caller.</summary>
public interface ISavedCardService
{
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, PayPalCardDetails card, string? alias,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedPaymentMethod>> GetCardsAsync(string buyerId,
        CancellationToken cancellationToken = default);

    Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
