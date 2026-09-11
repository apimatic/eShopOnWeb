using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Saved (vaulted) cards for a shopper. Cards are stored in PayPal's vault; this app keeps only
/// the vault token id + a safe descriptor. Every operation is scoped to the owning shopper.
/// </summary>
public interface ISavedCardService
{
    Task<Result<SavedPaymentMethod>> SaveCardAsync(string buyerId, GatewayCardDetails card, CancellationToken ct = default);
    Task<Result<IReadOnlyList<SavedPaymentMethod>>> ListCardsAsync(string buyerId, CancellationToken ct = default);
    Task<Result> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct = default);
}
