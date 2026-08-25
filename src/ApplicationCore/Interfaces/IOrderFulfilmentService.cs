using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Operator actions that follow a real payment: taking the money, releasing a hold, or giving money back.</summary>
public interface IOrderFulfilmentService
{
    /// <summary>Captures the held funds. Renews a stale authorization first if needed. Idempotent.</summary>
    Task<Order> FulfilAsync(int orderId, CancellationToken ct = default);

    /// <summary>Releases the hold before fulfilment. Idempotent.</summary>
    Task<Order> CancelAsync(int orderId, CancellationToken ct = default);

    /// <summary>Refunds a captured payment, in full or in part. Repeating the same idempotencyKey returns the same refund.</summary>
    Task<(Order Order, OrderRefund Refund)> RefundAsync(int orderId, decimal? amount, string idempotencyKey,
        string? note, CancellationToken ct = default);
}
