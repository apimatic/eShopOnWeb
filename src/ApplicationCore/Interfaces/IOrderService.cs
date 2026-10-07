using System.Collections.Generic;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Services;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);

    /// <summary>
    /// Places an order for <paramref name="buyerId"/> directly from catalog items, priced from the catalog.
    /// </summary>
    Task<Result<Order>> CreateOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address shippingAddress);
}
