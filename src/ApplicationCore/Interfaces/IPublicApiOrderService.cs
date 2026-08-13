using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places and moves orders through their lifecycle from the public API, reusing the app's existing
/// order/order-item model, and drives the SMS notifications that go out as an order moves. A
/// notification that cannot be sent never fails the underlying order action.
/// </summary>
public interface IPublicApiOrderService
{
    /// <summary>Places an order for the shopper from catalog item ids and quantities, and notifies them.</summary>
    Task<OrderOperationResult> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineItem> items, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an order dispatched (operator action). Notifies the shopper and queues a follow-up with
    /// the provider for a few days later. Returns null if the order does not exist.
    /// </summary>
    Task<OrderOperationResult?> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels an order (operator action). Notifies the shopper and calls off any queued follow-up
    /// before it goes out. Returns null if the order does not exist.
    /// </summary>
    Task<OrderOperationResult?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Lists the shopper's own orders, each with where its notifications got to (refreshed).</summary>
    Task<IReadOnlyList<OrderOperationResult>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);
}

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record OrderLineItem(int CatalogItemId, int Quantity);
