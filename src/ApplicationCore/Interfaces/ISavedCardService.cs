using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved cards (Flow 2). Cards live in PayPal's vault; this app keeps only the
/// token id and a safe descriptor. Every operation is scoped to the calling shopper.
/// </summary>
public interface ISavedCardService
{
    Task<SavedCard> SaveCardAsync(string buyerId, CardDetails card, string? label,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedCard>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Remove a saved card. Returns false when the caller has no such card.</summary>
    Task<bool> DeleteAsync(string buyerId, int savedCardId, CancellationToken cancellationToken = default);
}
