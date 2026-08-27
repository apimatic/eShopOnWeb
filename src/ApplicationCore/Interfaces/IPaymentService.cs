using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPaymentService
{
    /// <summary>
    /// Authorizes (holds) the order total, either with one-off card details or a saved card.
    /// Idempotent: repeating the call for an already-authorized order returns the existing payment.
    /// </summary>
    Task<Payment> AuthorizePaymentAsync(string buyerId, int orderId, CardDetails? card, int? savedCardId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Captures the held funds at fulfilment. Renews a stale authorization when possible.
    /// Idempotent: repeating the call for an already-captured order returns the existing payment.
    /// </summary>
    Task<Payment> CapturePaymentAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases held funds (voids the authorization) and cancels the order before fulfilment.
    /// </summary>
    Task<Payment?> CancelPaymentAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refunds a captured payment in full (amount null) or in part.
    /// Idempotent on the caller-supplied key: a repeated key returns the original refund.
    /// </summary>
    Task<PaymentRefund> RefundPaymentAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<Payment?> GetPaymentForOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);
}
