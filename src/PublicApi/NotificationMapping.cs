using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi;

internal static class NotificationMapping
{
    public static NotificationDto ToDto(OrderNotification notification)
    {
        return new NotificationDto
        {
            NotificationId = notification.Id,
            OrderId = notification.OrderId,
            Kind = notification.Kind.ToString(),
            Body = notification.ContentRedacted ? null : notification.Body,
            ContentRedacted = notification.ContentRedacted,
            ProviderMessageSid = notification.ProviderMessageSid,
            DeliveryStatus = notification.ProviderStatus,
            ErrorCode = notification.ProviderErrorCode,
            ErrorMessage = notification.ProviderErrorMessage,
            ScheduledFor = notification.ScheduledFor,
            CreatedAt = notification.CreatedAt,
            SourceNotificationId = notification.SourceNotificationId
        };
    }

    public static IReadOnlyList<NotificationDto> ToDto(IEnumerable<OrderNotification> notifications)
        => notifications.Select(ToDto).ToList();
}

internal static class OrderItemMapping
{
    public static OrderItemDto ToDto(OrderItem item)
    {
        return new OrderItemDto
        {
            CatalogItemId = item.ItemOrdered.CatalogItemId,
            ProductName = item.ItemOrdered.ProductName,
            UnitPrice = item.UnitPrice,
            Units = item.Units
        };
    }
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}
