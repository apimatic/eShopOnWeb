using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the money movement for an order: authorize (hold), fulfil (capture), cancel (void) and refund.
/// Shopper-scoped operations verify the order belongs to the caller; operator operations act on any order.
/// </summary>
public interface IPaymentService
{
    /// <summary>
    /// Authorize (place a hold for) the order total, paying either with a one-off card or a saved card.
    /// Shopper-scoped: <paramref name="buyerId"/> must own the order. Idempotent: a repeat returns the existing hold.
    /// </summary>
    Task<Payment> AuthorizeAsync(
        int orderId, string buyerId, CardDetails? card, int? savedPaymentMethodId, CancellationToken ct = default);

    /// <summary>Operator action: fulfil the order, capturing the money (renewing a stale authorization if needed).</summary>
    Task<Payment> FulfilAsync(int orderId, CancellationToken ct = default);

    /// <summary>Operator action: cancel the order before fulfilment, releasing the held funds.</summary>
    Task<Payment> CancelAsync(int orderId, CancellationToken ct = default);

    /// <summary>
    /// Shopper-scoped: refund the captured payment, in full or in part. The caller-supplied
    /// <paramref name="idempotencyKey"/> makes a repeat return the same refund; distinct keys allow distinct partials.
    /// </summary>
    Task<PaymentRefund> RefundAsync(
        int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken ct = default);
}
