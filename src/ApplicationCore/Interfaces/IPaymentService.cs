using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentService
{
    /// <summary>
    /// Places a hold on the order total (PayPal authorization). Idempotent: repeating the call
    /// for an already-authorized order returns the existing authorization instead of holding twice.
    /// </summary>
    Task<Payment> AuthorizePaymentAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes the money at fulfilment. Renews a stale authorization when PayPal allows it;
    /// otherwise fails with an operator-actionable error. Idempotent.
    /// </summary>
    Task<Payment> CapturePaymentAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Releases the held funds before fulfilment. Idempotent. Returns the payment, if one exists.</summary>
    Task<Payment?> VoidPaymentAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refunds a captured payment in full or in part. Repeating under the same idempotency key
    /// returns the original refund; a distinct key performs a new (legitimate) partial refund.
    /// </summary>
    Task<PaymentRefund> RefundPaymentAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default);
}
