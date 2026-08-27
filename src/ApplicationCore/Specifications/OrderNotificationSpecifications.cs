using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class NotificationsByOrderSpecification : Specification<OrderNotification>
{
    public NotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
            .OrderBy(n => n.CreatedUtc);
    }
}

public sealed class NotificationByIdempotencyKeySpecification : Specification<OrderNotification>
{
    public NotificationByIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}

public sealed class ScheduledNotificationsByContactNumberSpecification : Specification<OrderNotification>
{
    public ScheduledNotificationsByContactNumberSpecification(int contactNumberId)
    {
        Query.Where(n => n.ContactNumberId == contactNumberId
            && n.Status == "scheduled"
            && n.MessageSid != null);
    }
}

public sealed class ScheduledNotificationsByOrderSpecification : Specification<OrderNotification>
{
    public ScheduledNotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.Status == "scheduled"
            && n.MessageSid != null);
    }
}

public sealed class NotificationsCreatedBetweenSpecification : Specification<OrderNotification>
{
    public NotificationsCreatedBetweenSpecification(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        Query.Where(n => n.CreatedUtc >= fromUtc && n.CreatedUtc <= toUtc);
    }
}
