using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Places an order for a shopper directly from catalog items, reusing the app's existing
/// Order / OrderItem model rather than a parallel one.
/// </summary>
public interface IOrderPlacementService
{
    Task<Order> PlaceOrderAsync(string buyerId, IEnumerable<OrderLine> lines);
}

/// <summary>A requested line of an order: a catalog item and a quantity.</summary>
public record OrderLine(int CatalogItemId, int Quantity);
