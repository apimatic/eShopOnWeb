using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record OrderFulfillmentResult(Order Order, IReadOnlyList<OrderNotification> Notifications);

public record ShopperOrderView(Order Order, IReadOnlyList<OrderNotification> Notifications);

public interface IOrderFulfillmentService
{
    Task<OrderFulfillmentResult> DispatchAsync(int orderId, CancellationToken cancellationToken);
    Task<OrderFulfillmentResult> CancelAsync(int orderId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ShopperOrderView>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderNotification>> ListNotificationsAsync(int orderId, string? buyerId, bool isAdministrator, CancellationToken cancellationToken);
}
