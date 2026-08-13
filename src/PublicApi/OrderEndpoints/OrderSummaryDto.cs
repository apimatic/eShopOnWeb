using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public record OrderLineDto(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);

/// <summary>An order together with where each of its notifications got to.</summary>
public record OrderSummaryDto(
    int OrderId,
    System.DateTimeOffset OrderDate,
    decimal Total,
    IReadOnlyList<OrderLineDto> Items,
    IReadOnlyList<NotificationSummary> Notifications)
{
    public static OrderSummaryDto From(Order order, IReadOnlyList<NotificationSummary> notifications) => new(
        OrderId: order.Id,
        OrderDate: order.OrderDate,
        Total: order.Total(),
        Items: order.OrderItems
            .Select(i => new OrderLineDto(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units))
            .ToList(),
        Notifications: notifications);
}
