using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);

    /// <summary>Creates an order for a buyer directly from catalog item ids and quantities.</summary>
    Task<Order> CreateOrderFromItemsAsync(string buyerId, Address shippingAddress, IReadOnlyDictionary<int, int> itemQuantities);
}
