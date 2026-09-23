using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the pay-for-an-order flow: placing an order, authorizing (holding) the money, capturing at
/// fulfilment, cancelling (voiding) before fulfilment, refunding after, and reconciling against PayPal.
/// Shopper-scoped operations take the caller's buyer id and act only on that shopper's data.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order from catalog items for the shopper. The order starts awaiting payment.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines,
        Address? shipToAddress, CancellationToken ct);

    /// <summary>Authorizes the order total against a card or a saved card (idempotent, ownership-checked).</summary>
    Task<OrderPayment> PayAsync(string buyerId, int orderId, PayCommand command, CancellationToken ct);

    /// <summary>Operator action: fulfils the order, capturing the held funds (renewing a stale hold first if needed).</summary>
    Task<OrderPayment> FulfilAsync(int orderId, CancellationToken ct);

    /// <summary>Operator action: cancels before fulfilment, releasing the held funds.</summary>
    Task<OrderPayment> CancelAsync(int orderId, CancellationToken ct);

    /// <summary>Refunds the captured payment in full or in part, keyed by a caller-supplied idempotency key.</summary>
    Task<OrderRefund> RefundAsync(string buyerId, int orderId, string idempotencyKey, decimal? amount,
        CancellationToken ct);

    /// <summary>The caller's orders with their payment state.</summary>
    Task<IReadOnlyList<OrderWithPayment>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct);

    /// <summary>Loads one order's payment, checking it belongs to the caller.</summary>
    Task<OrderWithPayment?> GetOrderForBuyerAsync(string buyerId, int orderId, CancellationToken ct);

    /// <summary>Operator action: reconciles PayPal's transaction record against eShop payments for a range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
