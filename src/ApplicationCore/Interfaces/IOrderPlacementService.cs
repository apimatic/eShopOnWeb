using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places an order directly from catalog item ids and quantities (no basket), reusing the app's
/// existing Order/OrderItem model, and triggers the order-placed notification.
/// </summary>
public interface IOrderPlacementService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, CancellationToken cancellationToken = default);
}

/// <summary>One requested line of an order: a catalog item and how many of it.</summary>
public record OrderLine(int CatalogItemId, int Quantity);
