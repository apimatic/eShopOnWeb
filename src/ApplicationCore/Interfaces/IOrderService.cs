using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record CatalogQuantity(int CatalogItemId, int Quantity);

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);

    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<CatalogQuantity> items, Address shippingAddress);

    Task DispatchAsync(int orderId);

    Task CancelAsync(int orderId);
}
