using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A requested line for an API-placed order: a catalog item and how many.</summary>
public record OrderLine(int CatalogItemId, int Quantity);

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);

    /// <summary>
    /// Places an order for a buyer directly from catalog item ids and quantities, pricing each line
    /// from the current catalog price. Reuses the existing Order/OrderItem model. The order starts
    /// awaiting payment.
    /// </summary>
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address shippingAddress);
}
