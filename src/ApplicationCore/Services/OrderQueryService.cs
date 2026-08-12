using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Shopper-scoped reads over a caller's own orders and notifications. Each read brings non-terminal
/// notifications' delivery outcomes up to date from the provider before returning them.
/// </summary>
public class OrderQueryService : IOrderQueryService
{
    private readonly IReadRepository<Order> _orders;
    private readonly IRepository<Notification> _notifications;
    private readonly IOrderNotificationService _notificationService;

    public OrderQueryService(
        IReadRepository<Order> orders,
        IRepository<Notification> notifications,
        IOrderNotificationService notificationService)
    {
        _orders = orders;
        _notifications = notifications;
        _notificationService = notificationService;
    }

    public async Task<IReadOnlyList<OrderSummary>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var notifications = await _notifications.ListAsync(new NotificationsByBuyerSpecification(buyerId), cancellationToken);

        await RefreshAllAsync(notifications, cancellationToken);

        var byOrder = notifications
            .Where(n => n.OrderId.HasValue)
            .GroupBy(n => n.OrderId!.Value)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<NotificationView>)g.Select(MapView).ToList());

        return orders
            .Select(o => new OrderSummary(
                o.Id,
                o.Status.ToString(),
                o.Total(),
                o.OrderDate,
                byOrder.TryGetValue(o.Id, out var views) ? views : new List<NotificationView>()))
            .ToList();
    }

    public async Task<OrderNotificationsResult> GetOrderNotificationsAsync(
        int orderId, string requestingBuyerId, bool isAdministrator, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            return new OrderNotificationsResult(ActionOutcome.NotFound, new List<NotificationView>());
        }

        // A shopper sees only their own order's notifications; operators may see any.
        if (order.BuyerId != requestingBuyerId && !isAdministrator)
        {
            return new OrderNotificationsResult(ActionOutcome.Forbidden, new List<NotificationView>());
        }

        var notifications = await _notifications.ListAsync(new NotificationsByOrderSpecification(orderId), cancellationToken);
        await RefreshAllAsync(notifications, cancellationToken);

        return new OrderNotificationsResult(ActionOutcome.Success, notifications.Select(MapView).ToList());
    }

    private async Task RefreshAllAsync(IEnumerable<Notification> notifications, CancellationToken cancellationToken)
    {
        foreach (var notification in notifications)
        {
            await _notificationService.RefreshDeliveryStateAsync(notification, cancellationToken);
        }
    }

    private static NotificationView MapView(Notification n) => new(
        n.Id,
        n.Kind.ToString(),
        n.Status,
        n.ProviderMessageSid,
        n.ProviderErrorCode,
        n.ProviderErrorMessage,
        n.ContentRedacted,
        n.ScheduledSendAt,
        n.CreatedAt,
        n.OrderId);
}
