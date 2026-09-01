using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages the shopper's saved cards. Full card details only ever travel to the
/// processor's vault; locally only safe display metadata is kept.
/// </summary>
public interface ISavedPaymentMethodService
{
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Removes a saved card locally and at the processor. Ownership is enforced.</summary>
    Task DeleteAsync(string buyerId, int savedPaymentMethodId, CancellationToken cancellationToken = default);
}
