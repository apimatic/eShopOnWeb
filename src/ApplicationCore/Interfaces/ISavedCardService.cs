using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Saved-card flow: save, list and remove a shopper's cards. Scoped to the caller.</summary>
public interface ISavedCardService
{
    Task<SavedCardView> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct);

    Task<IReadOnlyList<SavedCardView>> GetCardsAsync(string buyerId, CancellationToken ct);

    /// <summary>Removes a saved card; afterwards it is neither listed nor usable to pay.</summary>
    Task DeleteCardAsync(string buyerId, int savedCardId, CancellationToken ct);
}

/// <summary>Produces the reconciliation report.</summary>
public interface IReconciliationService
{
    Task<ReconciliationReport> ReconcileAsync(System.DateTimeOffset from, System.DateTimeOffset to, CancellationToken ct);
}
