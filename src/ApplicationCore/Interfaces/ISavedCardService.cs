using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Saves, lists and removes a shopper's vaulted cards. All operations are scoped to the caller.</summary>
public interface ISavedCardService
{
    Task<SavedCard> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken);

    Task<IReadOnlyList<SavedCard>> ListAsync(string buyerId, CancellationToken cancellationToken);

    /// <summary>Remove a saved card. Returns false if the caller has no such card. Afterwards it is unusable.</summary>
    Task<bool> DeleteAsync(int paymentMethodId, string buyerId, CancellationToken cancellationToken);
}
