using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the order + payment lifecycle over the existing order model and the PayPal
/// gateway. Shopper-scoped methods take the caller's <c>buyerId</c> and act only on that
/// caller's own orders; operator methods act across orders and are role-restricted at the edge.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order from catalog items. The order starts awaiting payment.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineInput> lines,
        ShippingAddressInput? shipTo, CancellationToken cancellationToken = default);

    /// <summary>Authorizes (holds) the order total. Idempotent: never authorizes twice.</summary>
    Task<Order> AuthorizeAsync(string buyerId, int orderId, AuthorizePaymentInput input,
        CancellationToken cancellationToken = default);

    /// <summary>Operator action: fulfils the order and captures the held funds (renewing a stale hold).</summary>
    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: cancels before fulfilment, releasing any hold.</summary>
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a captured payment, in full or in part, for the caller's own order.</summary>
    Task<Refund> RefundAsync(string buyerId, int orderId, RefundInput input,
        CancellationToken cancellationToken = default);

    /// <summary>The caller's orders with their payment state.</summary>
    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId,
        CancellationToken cancellationToken = default);

    /// <summary>Operator action: reconciles PayPal transactions against eShop orders for a range.</summary>
    Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
