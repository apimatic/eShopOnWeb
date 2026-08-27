using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Sends order-related SMS notifications. Messaging problems must never fail the
/// underlying order operation: every method is best-effort and does not throw
/// for provider failures.
/// </summary>
public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Refreshes the stored delivery outcome of an order's notifications from the provider.</summary>
    Task RefreshOrderNotificationsAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refreshes the stored delivery outcome of all of a buyer's notifications from the provider.</summary>
    Task RefreshBuyerNotificationsAsync(string buyerId, CancellationToken cancellationToken = default);
}
