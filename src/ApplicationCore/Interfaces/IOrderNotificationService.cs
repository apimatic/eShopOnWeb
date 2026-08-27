using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Sends and records the shopper-facing text messages that accompany an order's lifecycle.
/// Notification failures never fail the underlying order operation.
/// </summary>
public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken ct = default);
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken ct = default);
    Task NotifyOrderCancelledAsync(Order order, CancellationToken ct = default);
}
