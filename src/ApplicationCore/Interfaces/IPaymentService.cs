using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the money movement for an order across the domain and the payment gateway:
/// authorize a hold at checkout, capture it at fulfilment (renewing a stale hold if needed),
/// void it on cancel, and refund after fulfilment. Every operation is idempotent in effect.
/// </summary>
public interface IPaymentService
{
    /// <summary>
    /// Shopper action. Authorizes (holds) the order total using either raw card details for a
    /// one-off payment or one of the shopper's saved cards. Idempotent: a repeat returns the
    /// existing payment rather than authorizing twice.
    /// </summary>
    Task<Payment> AuthorizeOrderAsync(int orderId, string buyerId, CardDetails? card, int? savedPaymentMethodId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action. Fulfils the order and captures the held funds. Renews a stale authorization
    /// rather than failing; if it can no longer be renewed, throws with an operator-actionable message.
    /// Idempotent: a repeat returns the already-captured payment.
    /// </summary>
    Task<Payment> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operator action. Cancels the order before fulfilment, releasing any held funds. Idempotent.
    /// </summary>
    Task<Payment?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Shopper action. Refunds a captured payment in full or in part. The idempotency key makes a
    /// repeat a no-op; two distinct keys are two legitimate partial refunds. Never refunds beyond
    /// what was captured.
    /// </summary>
    Task<Payment> RefundOrderAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default);
}
