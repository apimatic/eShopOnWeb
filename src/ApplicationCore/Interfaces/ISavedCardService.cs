using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved (vaulted) cards. Full card details are never stored in the application
/// database — only PayPal's vault id and safe display fields. Every operation is scoped to the caller.
/// </summary>
public interface ISavedCardService
{
    Task<SavedCard> SaveAsync(string buyerId, CardDetails card, CancellationToken ct = default);
    Task<IReadOnlyList<SavedCard>> ListAsync(string buyerId, CancellationToken ct = default);
    Task DeleteAsync(int savedCardId, string buyerId, CancellationToken ct = default);
}
