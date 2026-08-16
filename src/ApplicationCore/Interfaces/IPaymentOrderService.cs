using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the money movement around an order: place, pay (authorize/hold), fulfil (capture),
/// cancel (void) and refund, plus the shopper's order list and the operator reconciliation report.
/// Each action is separately invocable and payment operations are idempotent in effect.
/// </summary>
public interface IPaymentOrderService
{
    /// <summary>Places an order from catalog items for the shopper; it starts awaiting payment.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address? shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Authorizes (holds) the order total against a one-off <paramref name="card"/> or a saved card
    /// (<paramref name="savedPaymentMethodId"/>). Idempotent: a repeat never authorizes twice.
    /// </summary>
    Task<Order> PayAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId, CancellationToken cancellationToken = default);

    /// <summary>Operator fulfilment: captures the held funds, renewing a stale hold first if needed.</summary>
    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator cancel before fulfilment: releases the held funds so no money moved.</summary>
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refunds a captured payment (full or partial) for the shopper's own order, keyed by a
    /// caller-supplied idempotency key so a repeat under the same key never refunds twice.
    /// </summary>
    Task<Refund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>The caller's orders with payment state, newest first.</summary>
    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Reconciles PayPal's transaction record for a date range against eShop's orders.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
