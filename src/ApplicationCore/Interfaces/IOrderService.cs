using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record CatalogItemQuantity(int CatalogItemId, int Quantity);

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);

    /// <summary>
    /// Places an order directly from catalog item ids and quantities (no basket involved).
    /// Prices are taken from the current catalog, never from the caller. The order starts
    /// in <see cref="OrderStatus.AwaitingPayment"/>.
    /// </summary>
    Task<Order> CreateOrderFromItemsAsync(string buyerId, Address shippingAddress, IReadOnlyList<CatalogItemQuantity> items, string currency);
}
