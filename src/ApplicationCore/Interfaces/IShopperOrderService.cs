using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class CatalogOrderLine
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public interface IShopperOrderService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogOrderLine> items);

    Task<Order> DispatchAsync(int orderId);

    Task<Order> CancelAsync(int orderId);

    Task<Order?> GetByIdAsync(int orderId);

    Task<IReadOnlyList<Order>> ListForBuyerAsync(string buyerId);
}
