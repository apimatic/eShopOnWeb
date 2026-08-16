using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Paypal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the pay-for-an-order flow: placing an order awaiting payment, authorizing (holding)
/// the total, fulfilling (capturing), cancelling (releasing) and refunding it.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order for the shopper from catalog items and quantities. Starts awaiting payment.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, PlaceOrderInput input, CancellationToken ct = default);

    /// <summary>Authorizes (holds) the order total using a one-off or saved card. Idempotent per order.</summary>
    Task<Order> AuthorizeAsync(string buyerId, int orderId, PayOrderInput input, CancellationToken ct = default);

    /// <summary>Operator action: fulfils the order, capturing the held funds (renewing a stale hold if needed).</summary>
    Task<Order> FulfilAsync(int orderId, CancellationToken ct = default);

    /// <summary>Operator action: cancels the order before fulfilment, releasing any held funds.</summary>
    Task<Order> CancelAsync(int orderId, CancellationToken ct = default);

    /// <summary>Refunds the captured payment, fully or partially, under a caller-supplied idempotency key.</summary>
    Task<(Order Order, OrderRefund Refund)> RefundAsync(string buyerId, int orderId, RefundOrderInput input, CancellationToken ct = default);

    /// <summary>Returns the caller's orders with their payment state, newest first.</summary>
    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct = default);

    /// <summary>Loads one of the caller's orders with full payment state, or throws if it is not theirs.</summary>
    Task<Order> GetOrderForBuyerAsync(string buyerId, int orderId, CancellationToken ct = default);
}
