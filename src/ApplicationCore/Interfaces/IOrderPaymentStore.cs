using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Persistence for orders and their payment records. The claim save is the duplicate-charge guard: it inserts the
/// new payment attempt / refund row whose primary key a concurrent request would also try to insert.
/// </summary>
public interface IOrderPaymentStore
{
    /// <summary>The order with its items, payment attempts, refunds and provider responses; null when it does not exist.</summary>
    Task<Order?> GetOrderAsync(int orderId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the order together with the payment attempt or refund just started on it. Returns false — and saves
    /// nothing — when the store refuses the claim because another request already holds that key.
    /// </summary>
    Task<bool> TrySaveClaimAsync(Order order, CancellationToken cancellationToken);

    Task SaveAsync(Order order, CancellationToken cancellationToken);
}
