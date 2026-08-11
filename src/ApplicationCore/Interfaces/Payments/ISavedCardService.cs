using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// Manages a shopper's vaulted cards. A saved card belongs to the shopper who saved it; one shopper
/// never sees, uses or deletes another's. Full card details live only in PayPal's vault.
/// </summary>
public interface ISavedCardService
{
    Task<SavedCardSummary> SaveCardAsync(string buyerId, CardDetails card, string? label, CancellationToken ct = default);

    Task<IReadOnlyList<SavedCardSummary>> ListAsync(string buyerId, CancellationToken ct = default);

    /// <summary>Remove a saved card. Returns false if the caller has no such card.</summary>
    Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct = default);
}
