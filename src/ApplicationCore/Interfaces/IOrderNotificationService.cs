using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    /// <summary>Tells the shopper their order was placed. Never throws; messaging failures are recorded.</summary>
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Tells the shopper the order is on its way and queues a delivery follow-up with the provider.</summary>
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Tells the shopper the order was cancelled and cancels any not-yet-sent follow-up.</summary>
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Lists notifications for an order, refreshing non-terminal delivery outcomes from the provider.</summary>
    Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refreshes non-terminal delivery outcomes from the provider for the given notifications.</summary>
    Task RefreshStatusesAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default);

    Task<OrderNotification?> GetByIdAsync(int notificationId, CancellationToken cancellationToken = default);
}
