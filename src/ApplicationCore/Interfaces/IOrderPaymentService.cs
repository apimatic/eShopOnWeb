using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>Outcome of paying an order.</summary>
public record PayOrderResult(Order Order, string? CardBrand, string? Last4, bool AlreadyPaid);

/// <summary>Outcome of refunding an order.</summary>
public record RefundOrderResult(Order Order, string? RefundId, bool AlreadyRefunded);

/// <summary>
/// Order placement and PayPal payment lifecycle for a shopper. All operations enforce that the order
/// belongs to the caller and are idempotent in effect (a repeated pay/refund never double-charges or
/// double-refunds).
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places a new order (awaiting payment) from catalog items, priced from the catalog.</summary>
    Task<Order> PlaceOrderAsync(
        string buyerId, Address shipToAddress, IReadOnlyCollection<OrderLine> lines, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pays an order with PayPal, using either one-off <paramref name="card"/> details or a saved card
    /// (<paramref name="savedPaymentMethodId"/>). Exactly one must be supplied.
    /// </summary>
    Task<PayOrderResult> PayOrderAsync(
        string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId, CancellationToken cancellationToken = default);

    /// <summary>Issues a full refund of an order's captured payment.</summary>
    Task<RefundOrderResult> RefundOrderAsync(
        string buyerId, int orderId, CancellationToken cancellationToken = default);

    /// <summary>Lists the caller's orders (with items) and their payment state.</summary>
    Task<IReadOnlyList<Order>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
}
