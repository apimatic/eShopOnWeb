using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Saves, lists and removes a shopper's vaulted cards. All operations are scoped to the caller.</summary>
public interface ISavedCardService
{
    Task<SavedCard> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SavedCard>> GetCardsAsync(string buyerId, CancellationToken cancellationToken = default);
    Task DeleteCardAsync(string buyerId, int cardId, CancellationToken cancellationToken = default);
}
