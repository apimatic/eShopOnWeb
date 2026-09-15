using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ICatalogOrderService
{
    Task<Order> PlaceAsync(string buyerId, IReadOnlyList<CatalogOrderItem> items, Address shippingAddress, CancellationToken ct);
    Task DispatchAsync(int orderId, CancellationToken ct);
    Task CancelAsync(int orderId, CancellationToken ct);
    Task<IReadOnlyList<Order>> ListForBuyerAsync(string buyerId, CancellationToken ct);
    Task<Order> GetForBuyerAsync(int orderId, string buyerId, CancellationToken ct);
    Task<Order> GetByIdAsync(int orderId, CancellationToken ct);
}

public sealed record CatalogOrderItem(int CatalogItemId, int Quantity);
