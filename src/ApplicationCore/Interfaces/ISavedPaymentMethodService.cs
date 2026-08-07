using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved cards. Every operation is scoped to the owning shopper:
/// one shopper can never see, use or delete another's card.
/// </summary>
public interface ISavedPaymentMethodService
{
    /// <summary>Vaults a card and stores its safe reference for the shopper.</summary>
    Task<SavedPaymentMethod> SaveCardAsync(
        string buyerId, CardPaymentDetails card, CancellationToken cancellationToken = default);

    /// <summary>Lists the shopper's saved cards.</summary>
    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(
        string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes one of the shopper's saved cards. Afterwards it no longer appears in
    /// their list and can no longer be used to pay.
    /// </summary>
    Task DeleteAsync(string buyerId, int savedPaymentMethodId, CancellationToken cancellationToken = default);
}
