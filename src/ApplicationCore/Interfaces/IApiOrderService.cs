using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

/// <summary>
/// Places and moves orders through the API surface, reusing the app's existing Order/OrderItem
/// model. Placing, dispatching and cancelling each raise the appropriate shopper notification, but
/// a notification that cannot be sent never fails the underlying order operation.
/// </summary>
public interface IApiOrderService
{
    /// <summary>Places an order for the shopper from catalog items, then notifies them it was placed.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, CancellationToken cancellationToken = default);

    /// <summary>Marks an order dispatched and notifies the shopper (and queues a delivery follow-up). Null if the order does not exist.</summary>
    Task<Order?> DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Cancels an order, calls off any pending follow-up, and notifies the shopper. Null if the order does not exist.</summary>
    Task<Order?> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<Order?> GetOrderForBuyerAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);
}
