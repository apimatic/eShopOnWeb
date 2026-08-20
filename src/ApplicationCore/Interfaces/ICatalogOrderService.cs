using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ICatalogOrderService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogOrderItemRequest> items, CancellationToken cancellationToken);
}

public sealed record CatalogOrderItemRequest(int CatalogItemId, int Quantity);
