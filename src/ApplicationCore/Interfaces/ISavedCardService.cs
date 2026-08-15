using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Saves and manages a shopper's reusable cards. All calls are scoped to the caller's
/// <c>buyerId</c>; one shopper can never see, use, or delete another's card.
/// </summary>
public interface ISavedCardService
{
    Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, string? alias, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    Task DeleteAsync(int paymentMethodId, string buyerId, CancellationToken cancellationToken = default);
}
