using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>An eShop order paired with its payment state (for listing).</summary>
public record OrderWithPayment(Order Order, OrderPayment? Payment);

/// <summary>
/// Orchestrates the pay-for-an-order flow over the existing Order aggregate and the PayPal processor,
/// owning idempotency and per-shopper/operator scoping.
/// </summary>
public interface IPaymentService
{
    /// <summary>Place an order from catalog items for the given shopper; returns the new order id.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> items,
        Address shipToAddress, CancellationToken ct = default);

    /// <summary>Authorize (hold) the order total, using a one-off card or one of the shopper's saved cards.</summary>
    Task<OrderPayment> PayAsync(int orderId, string buyerId, CardDetails? card,
        int? savedPaymentMethodId, CancellationToken ct = default);

    /// <summary>Operator fulfils the order: capture the held funds (renewing a stale hold if needed).</summary>
    Task<OrderPayment> FulfilAsync(int orderId, CancellationToken ct = default);

    /// <summary>Operator cancels before fulfilment: release the held funds.</summary>
    Task<OrderPayment> CancelAsync(int orderId, CancellationToken ct = default);

    /// <summary>Shopper refunds their captured order, in full or in part, under an idempotency key.</summary>
    Task<PaymentRefund> RefundAsync(int orderId, string buyerId, decimal? amount,
        string idempotencyKey, CancellationToken ct = default);

    /// <summary>The caller's own orders with their payment state.</summary>
    Task<IReadOnlyList<OrderWithPayment>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct = default);

    /// <summary>Operator reconciliation report over a date range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

/// <summary>A requested order line: a catalog item id and quantity.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);
