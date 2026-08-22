using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

internal static class OrderApiMapper
{
    public static OrderNotificationDto ToDto(OrderNotification notification) =>
        new()
        {
            NotificationId = notification.Id,
            Kind = notification.Kind.ToString(),
            Status = notification.Status,
            ProviderSid = notification.ProviderSid,
            Body = notification.ContentDisposed ? null : notification.Body,
            ContentDisposed = notification.ContentDisposed,
            ErrorCode = notification.ErrorCode,
            ErrorMessage = notification.ErrorMessage,
            ParentNotificationId = notification.ParentNotificationId,
            CreatedAt = notification.CreatedAt.ToString("O"),
            ScheduledFor = notification.ScheduledFor?.ToString("O")
        };

    public static OrderSummaryDto ToSummary(
        Order order,
        System.Collections.Generic.IReadOnlyList<OrderNotification> notifications) =>
        new()
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            OrderDate = order.OrderDate.ToString("O"),
            Notifications = notifications.Select(ToDto).ToList()
        };
}
