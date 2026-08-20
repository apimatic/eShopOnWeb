using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Manages a shopper's saved (vaulted) cards. A saved card belongs to the shopper who saved it: one shopper
/// never sees, uses, or deletes another's.
/// </summary>
public interface IPaymentMethodService
{
    /// <summary>Vaults a card for the caller and returns its safe descriptor and id.</summary>
    Task<Result<PaymentMethodViewModel>> SaveCardAsync(
        string buyerId,
        CardDetails card,
        CancellationToken cancellationToken);

    /// <summary>The caller's saved cards.</summary>
    Task<IReadOnlyList<PaymentMethodViewModel>> ListForBuyerAsync(
        string buyerId,
        CancellationToken cancellationToken);

    /// <summary>Removes one of the caller's saved cards, at PayPal and locally, so it can no longer be used.</summary>
    Task<Result> DeleteCardAsync(
        string buyerId,
        int paymentMethodId,
        CancellationToken cancellationToken);
}
