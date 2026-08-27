using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISavedCardService
{
    /// <summary>Vaults a card with the provider and stores only its token id and safe display fields.</summary>
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct);

    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken ct);

    /// <summary>
    /// Deletes a saved card at the provider and locally. Returns false when the card does not
    /// exist or belongs to another buyer.
    /// </summary>
    Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken ct);
}
