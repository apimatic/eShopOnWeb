using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved (vaulted) cards. Every operation is scoped to the caller: one shopper can
/// never see, use, or delete another's card.
/// </summary>
public interface IPaymentMethodService
{
    Task<Result<SavedCardView>> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct = default);

    Task<Result<IReadOnlyList<SavedCardView>>> ListCardsAsync(string buyerId, CancellationToken ct = default);

    Task<Result> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct = default);
}
