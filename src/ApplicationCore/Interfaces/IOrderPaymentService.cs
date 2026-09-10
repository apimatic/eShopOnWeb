using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>One catalog line on a placed order.</summary>
public record OrderLineInput(int CatalogItemId, int Quantity);

/// <summary>
/// Orchestrates the money-movement flows for an order: place, authorize (pay), fulfil (capture), cancel
/// (void), refund, and the operator reconciliation report. Shopper-scoped operations act only on the
/// caller's own orders; operator operations (fulfil/cancel/reconcile) are unrestricted here and gated by
/// role at the API boundary.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Place an order from catalog items; it starts awaiting payment.</summary>
    Task<PlacedOrder> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines, Address shipToAddress,
        CancellationToken ct);

    /// <summary>Authorize (hold) the order total using one-off card details or a saved card.</summary>
    Task<OrderPaymentView> PayAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId,
        CancellationToken ct);

    /// <summary>Operator: fulfil the order, capturing the held funds (renewing a stale hold if needed).</summary>
    Task<OrderPaymentView> FulfilAsync(int orderId, CancellationToken ct);

    /// <summary>Operator: cancel before fulfilment, releasing the held funds.</summary>
    Task<OrderPaymentView> CancelAsync(int orderId, CancellationToken ct);

    /// <summary>Refund a captured payment, in full or in part, under a caller-supplied idempotency key.</summary>
    Task<(OrderPaymentView Payment, RefundView Refund)> RefundAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, CancellationToken ct);

    /// <summary>The caller's own orders with their payment state.</summary>
    Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken ct);

    /// <summary>Operator: reconcile PayPal's transactions against eShop orders over a date range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
