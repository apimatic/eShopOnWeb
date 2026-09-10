using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A single catalog line on a new order.</summary>
public record OrderLineInput(int CatalogItemId, int Quantity);

/// <summary>
/// Orchestrates the paid-order flow on top of the existing Order model: place, authorize (hold),
/// fulfil (capture), cancel (void) and refund. Every money operation is idempotent in effect.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Place an order from catalog lines, priced from the catalog. Starts awaiting payment.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines, Address shipToAddress, CancellationToken ct = default);

    /// <summary>
    /// Authorize (hold) the order total with PayPal, paying by a one-off <paramref name="card"/> or
    /// by one of the shopper's saved cards (<paramref name="savedCardId"/>). Shopper-scoped: only
    /// the order's own buyer may authorize it. Idempotent: a second call after a successful hold
    /// returns without holding again.
    /// </summary>
    Task<Order> AuthorizeAsync(string buyerId, int orderId, CardDetails? card, int? savedCardId, CancellationToken ct = default);

    /// <summary>
    /// Operator action: capture the authorization (take the money) and mark the order fulfilled.
    /// A stale authorization is reauthorized first; one that can no longer be renewed surfaces an
    /// operator-actionable error. Idempotent: capturing an already-fulfilled order is a no-op.
    /// </summary>
    Task<Order> FulfilAsync(int orderId, CancellationToken ct = default);

    /// <summary>Operator action: void the authorization before fulfilment, releasing the held funds.</summary>
    Task<Order> CancelAsync(int orderId, CancellationToken ct = default);

    /// <summary>
    /// Shopper-scoped: refund the captured payment, full (null amount) or partial. The
    /// <paramref name="idempotencyKey"/> makes the request safe to retry; the payment can never be
    /// refunded beyond what was captured.
    /// </summary>
    Task<PaymentRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct = default);
}
