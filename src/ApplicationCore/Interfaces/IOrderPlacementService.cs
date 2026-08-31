using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPlacementService
{
    /// <summary>
    /// Places an order from catalog items for a buyer and notifies them it was placed.
    /// Throws <see cref="Exceptions.CatalogItemsNotFoundException"/> when an item id is unknown.
    /// </summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogOrderItem> items,
        Address shipToAddress, CancellationToken ct = default);
}

public record CatalogOrderItem(int CatalogItemId, int Quantity);
