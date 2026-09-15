using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record CatalogOrderLine(int CatalogItemId, int Quantity);

public interface ICatalogOrderService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogOrderLine> lines, CancellationToken cancellationToken = default);
}
