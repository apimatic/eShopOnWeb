using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>One item requested when placing an order: a catalog item and how many.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>A caller's order paired with its payment state.</summary>
public record OrderWithPayment(Order Order, OrderPayment? Payment);

/// <summary>
/// Orchestrates the pay-for-an-order flow: place, authorize (hold), fulfil (capture), cancel (void),
/// refund. Each action is separately invocable and idempotent in effect. Shopper-scoped actions take
/// the caller's <c>buyerId</c>; operator actions (fulfil/cancel) act on any order.
/// </summary>
public interface IPaymentService
{
    /// <summary>Places an order from catalog items for the shopper; starts it awaiting payment. Returns the payment record (carries the new order id, total, currency and state).</summary>
    Task<OrderPayment> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines,
        Address shipToAddress, CancellationToken ct);

    /// <summary>Authorizes the order total (holds funds). Idempotent: a double-click never holds twice.</summary>
    Task<OrderPayment> PayAsync(int orderId, string buyerId, PayInstruction instruction, CancellationToken ct);

    /// <summary>Operator: fulfils the order and captures the payment. Renews a stale hold if needed.</summary>
    Task<OrderPayment> FulfilAsync(int orderId, CancellationToken ct);

    /// <summary>Operator: cancels before fulfilment, releasing the held funds.</summary>
    Task<OrderPayment> CancelAsync(int orderId, CancellationToken ct);

    /// <summary>Refunds a captured payment (full or partial) for the shopper's own order.</summary>
    Task<(OrderPayment Payment, PaymentRefund Refund)> RefundAsync(int orderId, string buyerId,
        decimal? amount, string idempotencyKey, CancellationToken ct);

    /// <summary>The caller's orders with their payment state.</summary>
    Task<IReadOnlyList<OrderWithPayment>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct);
}
