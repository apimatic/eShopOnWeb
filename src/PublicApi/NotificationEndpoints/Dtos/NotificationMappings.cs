using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints.Dtos;

/// <summary>Maps domain entities to the API DTOs. Never surfaces a destination number.</summary>
public static class NotificationMappings
{
    public static ContactNumberDto ToDto(this ContactNumber contactNumber) => new()
    {
        ContactNumberId = contactNumber.Id,
        PhoneNumber = contactNumber.PhoneNumber,
        CreatedAt = contactNumber.CreatedAt
    };

    public static OrderNotificationDto ToDto(this OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        OrderId = notification.OrderId,
        Type = notification.Type.ToString(),
        MessageSid = notification.MessageSid,
        DeliveryStatus = notification.DeliveryStatus,
        ErrorCode = notification.ErrorCode,
        IsFollowUp = notification.IsFollowUp,
        ContentDisposed = notification.ContentDisposed,
        ScheduledSendAt = notification.ScheduledSendAt,
        CreatedAt = notification.CreatedAt,
        Body = notification.Body
    };

    public static ApiOrderDto ToDto(this OrderOperationResult result)
    {
        var order = result.Order;
        return new ApiOrderDto
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Items = order.OrderItems.Select(i => new ApiOrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Notifications = result.Notifications.Select(n => n.ToDto()).ToList()
        };
    }
}
