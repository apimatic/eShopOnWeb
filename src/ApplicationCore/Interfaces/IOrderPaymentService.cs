using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Services;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Drives the payment lifecycle of an order: authorize (hold) at checkout,
/// capture at fulfilment, void on cancel, refund on return.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>
    /// Puts a hold on the order total using either one-off card details or one of the
    /// shopper's saved cards. Idempotent: paying an already-authorized order returns the
    /// existing payment instead of authorizing again.
    /// </summary>
    Task<OrderPayment> AuthorizePaymentAsync(Order order, CardDetails? card, int? savedPaymentMethodId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes the held money. Renews a stale authorization first; throws
    /// <see cref="Exceptions.PaymentException"/> with an operator-actionable message when
    /// the hold can no longer be renewed. Idempotent: fulfilling twice captures once.
    /// </summary>
    Task<OrderPayment> CapturePaymentAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Releases the shopper's held funds before fulfilment.</summary>
    Task<OrderPayment?> VoidPaymentAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refunds a captured payment in full (amount == null) or in part. The idempotency key
    /// guarantees a repeated request under the same key never refunds twice.
    /// </summary>
    Task<PaymentRefund> RefundPaymentAsync(Order order, decimal? amount, string idempotencyKey,
        string? noteToPayer, CancellationToken cancellationToken = default);
}
