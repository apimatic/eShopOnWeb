using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPaymentService
{
    /// <summary>
    /// Places an authorization hold for the order total using either one-off card details
    /// or one of the shopper's saved cards. Idempotent: paying an already-authorized order
    /// returns the existing payment.
    /// </summary>
    Task<OrderPayment> PayAsync(string buyerId, int orderId, CardDetails? card, int? savedCardId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Captures the authorized funds (operator action at fulfilment). Renews a stale
    /// authorization first; throws AuthorizationNotRenewableException when renewal is no
    /// longer possible. Idempotent: fulfilling twice returns the existing capture.
    /// </summary>
    Task<OrderPayment> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases the shopper's held funds before fulfilment. Idempotent.
    /// </summary>
    Task<OrderPayment?> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refunds a captured payment in full or in part. The idempotency key guarantees a
    /// repeated request under the same key never refunds twice.
    /// </summary>
    Task<PaymentRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, string? note, CancellationToken cancellationToken = default);
}
