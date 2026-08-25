using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the payment lifecycle of an order (authorize/capture/void/refund) against
/// PayPal, keeping the order's own state and PayPal's ids/status in sync.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>
    /// Authorizes the order's total. Exactly one of <paramref name="card"/> or
    /// <paramref name="savedPaymentMethodId"/> must be supplied. Returns null if the order does
    /// not exist or does not belong to <paramref name="buyerId"/>.
    /// </summary>
    Task<Order?> PayAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId,
        CancellationToken ct);

    /// <summary>Captures the order's authorization, renewing it first if it has gone stale. Admin-only.</summary>
    Task<Order?> FulfilAsync(int orderId, CancellationToken ct);

    /// <summary>Voids the order's authorization before fulfilment. Admin-only.</summary>
    Task<Order?> CancelAsync(int orderId, CancellationToken ct);

    /// <summary>
    /// Refunds part or all of the captured amount. Returns the existing refund unchanged if
    /// <paramref name="idempotencyKey"/> was already used for this order.
    /// </summary>
    Task<Refund?> RequestRefundAsync(string buyerId, int orderId, decimal amount, string idempotencyKey,
        CancellationToken ct);
}
