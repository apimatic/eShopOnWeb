using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISavedCardService
{
    Task<SavedCard> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedCard>> ListSavedCardsAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Returns false when the card does not exist or belongs to another shopper.</summary>
    Task<bool> DeleteSavedCardAsync(string buyerId, int savedCardId, CancellationToken cancellationToken = default);
}
