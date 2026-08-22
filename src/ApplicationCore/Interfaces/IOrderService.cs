using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);
    Task<Order> CreateCatalogOrderAsync(string buyerId, IReadOnlyCollection<CatalogOrderLine> lines, CancellationToken cancellationToken = default);
}

public record CatalogOrderLine(int CatalogItemId, int Quantity);
