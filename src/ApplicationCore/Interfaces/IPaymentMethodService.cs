using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Saves, lists and removes a shopper's reusable cards, always scoped to the owning shopper.</summary>
public interface IPaymentMethodService
{
    Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedPaymentMethod>> ListAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Removes the shopper's card. Returns false if no such card belongs to the shopper.</summary>
    Task<bool> DeleteAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default);
}
