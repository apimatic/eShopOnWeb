using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Coordinates the order payment flow: placing an order awaiting payment, authorizing (holding) the
/// money, capturing at fulfilment, releasing on cancel, and refunding after fulfilment. Each action is
/// separately invocable and idempotent in effect.
/// </summary>
public interface IPaymentService
{
    /// <summary>Places an order for the shopper from catalog items, priced from the catalog, awaiting payment.</summary>
    Task<Order> PlaceOrderAsync(
        string buyerId, IEnumerable<OrderLineItem> lines, Address shipToAddress, CancellationToken ct = default);

    /// <summary>Authorizes (holds) the order total. Re-invoking on an already-authorized order is a no-op.</summary>
    Task<Order> AuthorizeOrderAsync(
        int orderId, string buyerId, PaymentInstrument instrument, CancellationToken ct = default);

    /// <summary>Operator action: fulfils the order and captures the money, renewing a stale hold if needed.</summary>
    Task<Order> FulfilOrderAsync(int orderId, CancellationToken ct = default);

    /// <summary>Operator action: cancels before fulfilment, releasing the hold so no money moved.</summary>
    Task<Order> CancelOrderAsync(int orderId, CancellationToken ct = default);

    /// <summary>
    /// Refunds one of the caller's own fulfilled orders, fully or partially. The caller-supplied
    /// idempotency key prevents a repeated request from refunding twice.
    /// </summary>
    Task<(Order Order, PaymentRefund Refund)> RefundOrderAsync(
        int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken ct = default);

    /// <summary>The caller's own orders with their payment state.</summary>
    Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default);

    /// <summary>One of the caller's own orders, or null if it is not theirs / does not exist.</summary>
    Task<Order?> GetMyOrderAsync(int orderId, string buyerId, CancellationToken ct = default);
}
