using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Flow 1 — placing, paying for, and refunding orders. All operations are scoped to a single
/// shopper (<c>buyerId</c>) so no shopper can act on another's order. Payment operations are
/// idempotent in effect: a double-click never produces a second charge or refund.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order from catalog items (prices come from the catalog). Starts awaiting payment.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines,
        Address shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>Pays for an order with PayPal, using a one-off card or a saved card. Returns the updated order.</summary>
    Task<Order> PayOrderAsync(int orderId, string buyerId, PaymentInstruction instruction,
        CancellationToken cancellationToken = default);

    /// <summary>Refunds an order's payment in full. Returns the updated order.</summary>
    Task<Order> RefundOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);
}
