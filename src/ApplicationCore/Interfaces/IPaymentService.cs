using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the money movement over the life of an order: place → authorize (hold) → fulfil
/// (capture) or cancel (void) → refund. Each step is separately invocable and idempotent in effect.
/// </summary>
public interface IPaymentService
{
    /// <summary>
    /// Places an order for <paramref name="buyerId"/> from catalog items, priced from the catalog,
    /// starting the payment in the awaiting-payment state. Returns the new order id.
    /// </summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, CancellationToken cancellationToken = default);

    /// <summary>
    /// Authorizes (holds) the order total, paying with either raw <paramref name="card"/> details or a
    /// saved card (<paramref name="paymentMethodId"/>). Idempotent: a payment already authorized is
    /// returned unchanged. <paramref name="buyerId"/> scopes the action to the order's owner.
    /// </summary>
    Task<Payment> AuthorizeAsync(int orderId, string buyerId, CardDetails? card, int? paymentMethodId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: fulfils the order, capturing the money (renewing a stale hold first if needed).</summary>
    Task<Payment> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: cancels before fulfilment, releasing the held funds.</summary>
    Task<Payment> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refunds the captured payment, in full (<paramref name="amount"/> null) or in part, bounded by
    /// what remains refundable. <paramref name="idempotencyKey"/> makes a repeat a no-op. Returns the
    /// updated payment; the created/echoed refund is found via <see cref="Payment.GetRefundByKey"/>.
    /// </summary>
    Task<Payment> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>The caller's own orders paired with their payment state.</summary>
    Task<IReadOnlyList<OrderPaymentSnapshot>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Loads a payment by order id scoped to its owner, or null. Used for read endpoints.</summary>
    Task<Payment?> GetPaymentForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);
}
