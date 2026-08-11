using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>An item to include when placing an order (a catalog item and a quantity).</summary>
public record PlaceOrderItem(int CatalogItemId, int Quantity);

/// <summary>An order paired with its payment (if any), for read views.</summary>
public record OrderWithPayment(Order Order, Payment? Payment);

/// <summary>Orchestrates the paid-order lifecycle over the existing order model and PayPal.</summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order from catalog items for the given shopper; returns the new order id.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<PlaceOrderItem> items, Address? shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Authorizes (holds) the order total using a one-off card or one of the shopper's saved cards.
    /// Idempotent: a repeat while already authorized returns the existing hold without re-authorizing.
    /// </summary>
    Task<Payment> AuthorizeAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: fulfils the order and captures the money, renewing a stale hold if needed.</summary>
    Task<Payment> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: cancels an order before fulfilment, releasing any held funds.</summary>
    Task<OrderWithPayment> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a captured payment in full or in part, keyed by a caller-supplied idempotency key.</summary>
    Task<Refund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Returns the caller's orders with their payment state.</summary>
    Task<IReadOnlyList<OrderWithPayment>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: reconciles PayPal's transactions against eShop orders for a date range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
