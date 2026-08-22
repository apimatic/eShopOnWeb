using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderNotificationService
{
    Task TryNotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);

    Task TryNotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);

    Task TryNotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    Task CancelPendingFollowUpsAsync(int orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task RefreshProviderStatusAsync(IEnumerable<OrderNotification> notifications, CancellationToken cancellationToken = default);

    Task<OrderNotification?> ResendAsync(int notificationId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<OrderNotification?> RedactContentAsync(int notificationId, CancellationToken cancellationToken = default);
}
