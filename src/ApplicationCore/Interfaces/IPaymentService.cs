using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the pay-for-an-order flow: place → authorize (hold) → fulfil (capture) or
/// cancel (void) or refund, plus the caller's order list and the operator reconciliation report.
/// </summary>
public interface IPaymentService
{
    /// <summary>Place an order from catalog items for the shopper; starts awaiting payment. Returns the new order id.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> items, ShippingAddressInput shipTo, CancellationToken cancellationToken);

    /// <summary>Authorize (hold) the order total using a one-off card or a saved card. Idempotent per order.</summary>
    Task<PaymentView> PayAsync(string buyerId, int orderId, PayInstruction instruction, CancellationToken cancellationToken);

    /// <summary>Operator: mark the order fulfilled and capture the held funds (renewing a stale hold first).</summary>
    Task<PaymentView> FulfilAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>Operator: cancel before fulfilment — release the held funds.</summary>
    Task<PaymentView> CancelAsync(int orderId, CancellationToken cancellationToken);

    /// <summary>Refund a captured payment, full or partial. Idempotent per caller-supplied key.</summary>
    Task<(string RefundId, PaymentView Payment)> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>The caller's orders with their payment state.</summary>
    Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken);

    /// <summary>Operator: reconcile PayPal's transaction record against eShop orders for a date range.</summary>
    Task<ReconciliationResult> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
