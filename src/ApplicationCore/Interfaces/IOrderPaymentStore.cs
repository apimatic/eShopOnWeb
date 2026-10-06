using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Persistence for an order's payment attempts and refunds. The claim methods insert a row whose
/// primary key can exist only once, so the store itself refuses a second concurrent claim.
/// </summary>
public interface IOrderPaymentStore
{
    /// <summary>Loads the order with its items, payment attempts and refunds, tracked for updates.</summary>
    Task<Order?> GetOrderWithPaymentsAsync(int orderId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Order>> ListOrdersWithPaymentsForBuyerAsync(string buyerId, CancellationToken cancellationToken);

    /// <summary>Inserts the attempt; returns false when another attempt already holds that (OrderId, AttemptNumber).</summary>
    Task<bool> TryClaimPaymentAttemptAsync(OrderPaymentAttempt attempt, CancellationToken cancellationToken);

    /// <summary>Inserts the refund; returns false when another refund already holds that (OrderId, Sequence).</summary>
    Task<bool> TryClaimRefundAsync(OrderRefund refund, CancellationToken cancellationToken);

    /// <summary>Reads the attempts straight from the store, bypassing anything this unit of work has cached.</summary>
    Task<IReadOnlyList<OrderPaymentAttempt>> ReadPaymentAttemptsAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>Reads the refunds straight from the store, bypassing anything this unit of work has cached.</summary>
    Task<IReadOnlyList<OrderRefund>> ReadRefundsAsync(int orderId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
