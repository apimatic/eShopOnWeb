using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved (vaulted) cards. Card details are vaulted at PayPal; only a
/// safe description and the vault token reference are kept by eShop. A card belongs to the
/// shopper who saved it — one shopper can never see, use or delete another's.
/// </summary>
public interface ISavedCardService
{
    Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, string? alias, CancellationToken ct = default);
    Task<IReadOnlyList<PaymentMethod>> GetCardsAsync(string buyerId, CancellationToken ct = default);
    Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct = default);
}
