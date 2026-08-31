using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Sends the shopper-facing SMS notifications as an order moves, and keeps the local
/// notification records in step with the provider. Every method is best-effort:
/// a messaging failure is logged and recorded, never thrown — the underlying order
/// operation must still succeed.
/// </summary>
public interface IOrderNotificationService
{
    Task NotifyOrderPlacedAsync(Order order, CancellationToken ct = default);
    Task NotifyOrderDispatchedAsync(Order order, CancellationToken ct = default);
    Task NotifyOrderCancelledAsync(Order order, CancellationToken ct = default);

    /// <summary>
    /// Calls off every not-yet-sent scheduled message addressed to the given number,
    /// so that a removed contact number is never messaged again.
    /// </summary>
    Task CancelScheduledMessagesToNumberAsync(string buyerId, string phoneNumber, CancellationToken ct = default);

    /// <summary>Best-effort refresh of each notification's delivery outcome from the provider.</summary>
    Task RefreshDeliveryOutcomesAsync(IReadOnlyCollection<OrderNotification> notifications, CancellationToken ct = default);
}
