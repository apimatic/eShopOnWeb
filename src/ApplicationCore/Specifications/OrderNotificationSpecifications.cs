using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationsByOrderSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
            .OrderBy(n => n.CreatedUtc);
    }
}

public class OrderNotificationsByOrderIdsSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrderIdsSpecification(IEnumerable<int> orderIds)
    {
        Query.Where(n => orderIds.Contains(n.OrderId))
            .OrderBy(n => n.CreatedUtc);
    }
}

public class OrderNotificationByIdempotencyKeySpecification : Specification<OrderNotification>
{
    public OrderNotificationByIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}

/// <summary>Follow-ups for an order that the provider has queued but not yet sent.</summary>
public class PendingFollowUpsByOrderSpecification : Specification<OrderNotification>
{
    public PendingFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.Type == OrderNotificationType.DeliveryFollowUp
            && n.Status == "scheduled");
    }
}

public class OrderNotificationsInRangeSpecification : Specification<OrderNotification>
{
    public OrderNotificationsInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.CreatedUtc >= from && n.CreatedUtc <= to)
            .OrderBy(n => n.CreatedUtc);
    }
}
