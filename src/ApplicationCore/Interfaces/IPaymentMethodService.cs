using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Saved-card management, scoped to the signed-in shopper.</summary>
public interface IPaymentMethodService
{
    Task<SavedCardView> SaveAsync(string buyerId, CardInput card, CancellationToken ct = default);

    Task<IReadOnlyList<SavedCardView>> ListAsync(string buyerId, CancellationToken ct = default);

    /// <summary>Remove a saved card. Returns false if the caller has no such card.</summary>
    Task<bool> DeleteAsync(string buyerId, string paymentMethodId, CancellationToken ct = default);
}
