using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentMethodService
{
    Task<SavedCard> SaveCardAsync(string buyerId, GatewayCardDetails card, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedCard>> GetSavedCardsAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the shopper's own saved card both locally and at the processor.
    /// Returns false when the card does not exist or belongs to someone else.
    /// </summary>
    Task<bool> DeleteSavedCardAsync(string buyerId, int savedCardId, CancellationToken cancellationToken = default);
}
