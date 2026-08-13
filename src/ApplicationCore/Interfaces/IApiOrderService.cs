using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places and moves orders that are driven directly through the API (as opposed to the storefront basket
/// checkout). It reuses the existing Order / OrderItem model and triggers the SMS notifications for each move.
/// </summary>
public interface IApiOrderService
{
    /// <summary>Place an order for a shopper from catalog item ids and quantities. Returns the new order's id.</summary>
    Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, Address shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>Operator marks an order dispatched (and queues the delivery follow-up). Returns null if the order does not exist.</summary>
    Task<Order?> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator cancels an order (and calls off any pending follow-up). Returns null if the order does not exist.</summary>
    Task<Order?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);
}

/// <summary>One requested order line: a catalog item and how many of it.</summary>
public record OrderLine(int CatalogItemId, int Quantity);
