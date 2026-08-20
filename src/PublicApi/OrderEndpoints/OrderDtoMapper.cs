using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

internal static class OrderDtoMapper
{
    public static MyOrderDto Map(Order order, IReadOnlyList<OrderNotification> notifications)
    {
        return new MyOrderDto
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Notifications = notifications.Select(ToDto).ToList()
        };
    }

    public static OrderNotificationDto ToDto(OrderNotification notification)
    {
        return new OrderNotificationDto
        {
            NotificationId = notification.Id,
            Kind = notification.Kind.ToString(),
            Body = notification.BodyRedacted ? string.Empty : notification.Body,
            ProviderSid = notification.ProviderSid ?? string.Empty,
            ProviderStatus = notification.ProviderStatus,
            ErrorCode = notification.ErrorCode,
            ErrorMessage = notification.ErrorMessage,
            DateCreated = notification.DateCreated,
            DateScheduled = notification.DateScheduled,
            BodyRedacted = notification.BodyRedacted
        };
    }
}
