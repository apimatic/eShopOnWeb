using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PlaceOrderItem(int CatalogItemId, int Quantity);

public interface IOrderWorkflowService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<PlaceOrderItem> items, Address? shipToAddress = null);

    Task<Order> DispatchAsync(int orderId);

    Task<Order> CancelAsync(int orderId);

    Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId);

    Task<Order?> GetBuyerOrderAsync(string buyerId, int orderId);

    Task<Order?> GetOrderAsync(int orderId);
}
