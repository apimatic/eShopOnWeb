using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPaymentService
{
    /// <summary>
    /// Places an authorization hold for the order total. Idempotent: paying an
    /// already-authorized order returns the existing payment.
    /// </summary>
    Task<Payment> AuthorizePaymentAsync(string buyerId, int orderId, CardDetails? card, int? savedCardId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Captures the authorized funds at fulfilment, renewing a stale
    /// authorization first when PayPal still allows it.
    /// </summary>
    Task<Payment> CapturePaymentForFulfilmentAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels an unfulfilled order, releasing the shopper's held funds.
    /// </summary>
    Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refunds a captured payment in full (amount omitted) or in part.
    /// Idempotent per idempotencyKey.
    /// </summary>
    Task<PaymentRefund> RefundPaymentAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default);
}
