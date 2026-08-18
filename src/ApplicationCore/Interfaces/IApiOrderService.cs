using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A line on a new order: a catalog item and how many of it.</summary>
public readonly record struct OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>
/// Places an order directly from catalog item ids and quantities (the API has no basket), reusing
/// the app's existing Order / OrderItem / CatalogItemOrdered model.
/// </summary>
public interface IApiOrderService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines, Address shipToAddress, CancellationToken cancellationToken = default);
}
