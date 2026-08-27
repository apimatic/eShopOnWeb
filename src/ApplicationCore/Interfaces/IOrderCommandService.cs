using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record OrderItemRequest(int CatalogItemId, int Units);

/// <summary>
/// Order lifecycle commands. Each transition also notifies the shopper on a
/// best-effort basis: a message that cannot be sent never fails the operation.
/// </summary>
public interface IOrderCommandService
{
    /// <summary>Places an order from catalog items and tells the shopper it was placed.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items, Address shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>Marks the order dispatched, tells the shopper, and queues the delivery follow-up with the provider.</summary>
    Task<Order?> DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Cancels the order, tells the shopper, and calls off any not-yet-sent follow-up.</summary>
    Task<Order?> CancelAsync(int orderId, CancellationToken cancellationToken = default);
}
