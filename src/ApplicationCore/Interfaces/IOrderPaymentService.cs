using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the money-movement lifecycle of an order over PayPal: place, authorize (hold),
/// fulfil (capture), cancel (void), refund, plus the caller's order list and the operator's
/// reconciliation report. Each action is separately invocable and idempotent in effect.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Place an order from catalog lines for the given buyer; returns the new order id.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, ShippingAddressInput? shipTo, CancellationToken cancellationToken = default);

    /// <summary>Authorize (hold) the order total for the caller's own order.</summary>
    Task<Order> AuthorizeAsync(string buyerId, int orderId, PayOrderCommand command, CancellationToken cancellationToken = default);

    /// <summary>Fulfil the order — capture the held funds (operator action).</summary>
    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Cancel the order before fulfilment — release the hold (operator action).</summary>
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refund the caller's own fulfilled order, in full or in part.</summary>
    Task<(Order Order, OrderRefund Refund)> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>The caller's own orders, with payment state.</summary>
    Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Reconcile PayPal's ledger against eShop orders for a date range (operator action).</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
