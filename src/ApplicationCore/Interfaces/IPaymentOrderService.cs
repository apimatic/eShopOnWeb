using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>An item to place on an order: a catalog item id and quantity.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>Optional shipping address for a placed order.</summary>
public record ShippingAddressRequest(string? Street, string? City, string? State, string? Country, string? ZipCode);

/// <summary>
/// Orchestrates the pay-for-an-order flow over the existing Order aggregate and the PayPal gateway.
/// Shopper operations act only on the caller's own orders; operator operations act on any order.
/// </summary>
public interface IPaymentOrderService
{
    /// <summary>Places an order for the shopper from catalog items. Starts awaiting payment.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines,
        ShippingAddressRequest? shipTo, CancellationToken ct);

    /// <summary>Authorizes (holds) the order total using one-off card details or a saved card.</summary>
    Task<Order> AuthorizeAsync(string buyerId, int orderId, CardDetails? card, int? savedCardId,
        CancellationToken ct);

    /// <summary>Operator: fulfils the order, capturing the funds (renewing a stale authorization first).</summary>
    Task<Order> FulfilAsync(int orderId, CancellationToken ct);

    /// <summary>Operator: cancels the order before fulfilment, releasing the held funds.</summary>
    Task<Order> CancelAsync(int orderId, CancellationToken ct);

    /// <summary>Refunds the captured payment, full or partial, idempotent under the caller's key.</summary>
    Task<(Order Order, OrderRefund Refund)> RefundAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, CancellationToken ct);

    /// <summary>The caller's orders with their payment state.</summary>
    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct);

    /// <summary>Operator: PayPal's transactions for a range, lined up against eShop orders.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
