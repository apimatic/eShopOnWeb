using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISavedCardService
{
    Task<SavedCard> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SavedCard>> ListAsync(string buyerId, CancellationToken cancellationToken = default);
    Task DeleteAsync(string buyerId, int savedCardId, CancellationToken cancellationToken = default);
}
