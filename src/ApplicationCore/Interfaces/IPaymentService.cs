using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentService
{
    /// <summary>
    /// Places a hold on the order total via PayPal (authorize, not capture).
    /// Idempotent: paying an already-authorized order returns the existing payment.
    /// </summary>
    Task<OrderPayment> AuthorizePaymentAsync(string buyerId, int orderId,
        PayPalCardDetails? card, int? paymentMethodId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Captures the authorized funds at fulfilment. Renews a stale authorization
    /// when possible. Idempotent: fulfilling an already-captured order returns
    /// the existing payment.
    /// </summary>
    Task<OrderPayment> CapturePaymentAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels an order before fulfilment, releasing the shopper's held funds.
    /// </summary>
    Task CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refunds a captured payment, in full or in part. The idempotency key is
    /// caller-supplied: repeating the same key returns the original refund.
    /// </summary>
    Task<PaymentRefund> RefundPaymentAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, string? noteToPayer, CancellationToken cancellationToken = default);
}
