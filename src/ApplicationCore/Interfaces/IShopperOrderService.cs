using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record PlaceOrderItem(int CatalogItemId, int Quantity);

public sealed record PlaceOrderAddress(string Street, string City, string State, string Country, string ZipCode);

public interface IShopperOrderService
{
    Task<Order> PlaceAsync(
        string buyerId,
        IReadOnlyList<PlaceOrderItem> items,
        PlaceOrderAddress? shipTo,
        CancellationToken cancellationToken = default);

    Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Order> GetForBuyerAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<Order> GetForOperatorAsync(int orderId, CancellationToken cancellationToken = default);
}
