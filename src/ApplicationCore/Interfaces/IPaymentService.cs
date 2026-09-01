using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentService
{
    /// <summary>
    /// Authorize (hold) the order total via PayPal, either with one-off card details or one of the
    /// shopper's saved cards. Idempotent: paying an already-authorized order returns the existing payment.
    /// </summary>
    Task<Payment> PayOrderAsync(string buyerId, int orderId, PayPalCard? card, int? savedPaymentMethodId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Capture the held funds at fulfilment. Renews a stale authorization when possible; throws a
    /// PaymentException with an operator-actionable message when it cannot be renewed.
    /// </summary>
    Task<Payment> FulfillOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Void the authorization before fulfilment, releasing the shopper's held funds.</summary>
    Task<Payment> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refund a captured payment in full (amount null) or in part. Repeating the same idempotencyKey
    /// returns the original refund without refunding twice.
    /// </summary>
    Task<PaymentRefundOutcome> RefundOrderAsync(int orderId, decimal? amount, string idempotencyKey,
        string? noteToPayer, CancellationToken cancellationToken = default);
}

public record PaymentRefundOutcome(Payment Payment, PaymentRefund Refund, bool AlreadyExisted);
