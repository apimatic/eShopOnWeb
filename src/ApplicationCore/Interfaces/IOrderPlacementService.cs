using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places an order directly from catalog items (item id + quantity), reusing the app's existing Order /
/// OrderItem model rather than a parallel one. The buyer's identity comes from the caller, not the request.
/// </summary>
public interface IOrderPlacementService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineItem> lines, Address? shipToAddress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record OrderLineItem(int CatalogItemId, int Quantity);
