using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places orders and drives their payment lifecycle (pay / refund) through PayPal. All operations are
/// scoped to a shopper (<c>buyerId</c>) so one shopper can never act on another's order, and the
/// pay/refund operations are idempotent in effect — a double-click never double-charges or double-refunds.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>
    /// Places an order for the shopper from catalog items, priced from the catalog, in the
    /// awaiting-payment state. Returns the persisted order (with its generated id).
    /// </summary>
    Task<Order> PlaceOrderAsync(string buyerId, IEnumerable<OrderLineRequest> lines, Address? shipToAddress = null, CancellationToken cancellationToken = default);

    /// <summary>Pays an awaiting-payment order via PayPal using a one-off card or a saved card.</summary>
    Task<Order> PayOrderAsync(string buyerId, int orderId, PaymentInstruction instruction, CancellationToken cancellationToken = default);

    /// <summary>Fully refunds a paid order via PayPal.</summary>
    Task<Order> RefundOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);

    /// <summary>All of the shopper's orders, with items and payment state.</summary>
    Task<IReadOnlyList<Order>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken = default);
}
