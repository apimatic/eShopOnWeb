using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the pay-for-an-order flow: place, authorize (pay), fulfil (capture), cancel (void),
/// refund, plus the shopper's my-orders view and the operator reconciliation report.
/// </summary>
public interface IPaymentService
{
    /// <summary>Places an order for the shopper from catalog items; returns the new order id.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines,
        Address? shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Authorizes the order total (holds the money). Funds it with a one-off card or a saved card.
    /// Idempotent: a second call once authorized returns the existing payment unchanged.
    /// </summary>
    Task<Payment> PayOrderAsync(string buyerId, int orderId, CardDetails? card, int? savedCardId,
        CancellationToken cancellationToken = default);

    /// <summary>Operator: fulfils the order and captures the held funds (renewing a stale hold first).</summary>
    Task<Payment> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator: cancels the order before fulfilment, releasing the hold.</summary>
    Task<Payment> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refunds the captured payment for the shopper's order, fully or partially.</summary>
    Task<Refund> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>The caller's orders with their payment state.</summary>
    Task<IReadOnlyList<OrderWithPayment>> GetOrdersForBuyerAsync(string buyerId,
        CancellationToken cancellationToken = default);

    /// <summary>Operator: reconciles PayPal's transaction record against eShop orders for a range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
