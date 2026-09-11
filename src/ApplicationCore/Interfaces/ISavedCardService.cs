using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Save, list and remove a shopper's cards. All operations are scoped to the caller.</summary>
public interface ISavedCardService
{
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, GatewayCardDetails card, CancellationToken ct = default);

    Task<IReadOnlyList<SavedPaymentMethod>> GetCardsAsync(string buyerId, CancellationToken ct = default);

    /// <summary>Removes a saved card. Returns false if not found / not owned by the caller.</summary>
    Task<bool> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct = default);
}
