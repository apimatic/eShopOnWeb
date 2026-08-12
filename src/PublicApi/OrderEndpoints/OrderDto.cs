using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>An order with where each of its notifications got to.</summary>
public record OrderDto(
    int OrderId,
    string Status,
    DateTimeOffset OrderDate,
    decimal Total,
    IReadOnlyList<NotificationDto> Notifications)
{
    public static OrderDto From(Order order, IReadOnlyList<OrderNotification> notifications) => new(
        order.Id,
        order.Status.ToString(),
        order.OrderDate,
        order.Total(),
        notifications.Select(NotificationDto.From).ToList());
}
