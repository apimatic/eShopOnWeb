using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Sends order lifecycle SMS notifications to the shopper's registered contact numbers.
/// All methods are best-effort: a messaging failure never fails the underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken cancellationToken = default);
    Task NotifyOrderCancelledAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pulls the latest delivery outcome from the provider for non-terminal notifications of an order.
    /// </summary>
    Task RefreshOrderNotificationStatusesAsync(int orderId, CancellationToken cancellationToken = default);
}
