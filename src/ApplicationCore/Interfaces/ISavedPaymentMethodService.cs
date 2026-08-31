using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISavedPaymentMethodService
{
    /// <summary>
    /// Vaults the card with PayPal and stores only safe display data locally.
    /// </summary>
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the saved card both locally and from PayPal's vault. Afterwards it is
    /// neither listed nor usable to pay.
    /// </summary>
    Task DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
