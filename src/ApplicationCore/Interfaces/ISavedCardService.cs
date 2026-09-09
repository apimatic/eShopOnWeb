using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Flow 2 — a shopper's saved cards. Cards are vaulted at PayPal; this app stores only the
/// opaque vault id and a safe descriptor. A card belongs to the shopper who saved it.
/// </summary>
public interface ISavedCardService
{
    Task<PaymentMethod> SaveCardAsync(string identity, CardDetails card, string? alias,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaymentMethod>> ListCardsAsync(string identity,
        CancellationToken cancellationToken = default);

    Task DeleteCardAsync(string identity, int paymentMethodId,
        CancellationToken cancellationToken = default);
}
