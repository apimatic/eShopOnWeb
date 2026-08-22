using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record CatalogItemQuantity(int CatalogItemId, int Quantity);

public record PlaceOrderResult(Order Order, IReadOnlyList<OrderNotification> Notifications);

public interface IOrderPlacementService
{
    Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CatalogItemQuantity> items,
        Address? shipToAddress,
        CancellationToken cancellationToken = default);
}
