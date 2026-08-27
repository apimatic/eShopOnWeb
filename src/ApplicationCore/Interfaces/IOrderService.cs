using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);
    Task<Order> CreateOrderFromCatalogItemsAsync(
        string buyerId,
        IReadOnlyCollection<CatalogQuantity> items,
        Address shippingAddress,
        CancellationToken cancellationToken = default);
}

public readonly record struct CatalogQuantity(int CatalogItemId, int Quantity);
