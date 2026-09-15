using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Saves, lists and removes a shopper's vaulted cards. Every operation is scoped to the caller.</summary>
public interface IPaymentMethodService
{
    Task<Result<SavedCardView>> SaveCardAsync(string buyerId, CardInput card, CancellationToken ct = default);

    Task<Result<IReadOnlyList<SavedCardView>>> GetCardsAsync(string buyerId, CancellationToken ct = default);

    Task<Result> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct = default);
}
