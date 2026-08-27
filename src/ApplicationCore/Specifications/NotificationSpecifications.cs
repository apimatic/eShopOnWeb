using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsByOrderSpecification : Specification<OrderNotification>
{
    public NotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
            .OrderBy(n => n.Id);
    }
}

public class NotificationByIdempotencyKeySpecification : Specification<OrderNotification>
{
    public NotificationByIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}

/// <summary>
/// Notifications for an order whose provider message is still scheduled, i.e.
/// queued with the provider and not yet sent.
/// </summary>
public class ScheduledNotificationsForOrderSpecification : Specification<OrderNotification>
{
    public ScheduledNotificationsForOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId && n.Status == "scheduled" && n.MessageSid != null);
    }
}

/// <summary>
/// Notifications addressed through a contact number whose provider message is
/// still scheduled — used so a deleted number is never messaged again.
/// </summary>
public class ScheduledNotificationsForContactNumberSpecification : Specification<OrderNotification>
{
    public ScheduledNotificationsForContactNumberSpecification(int contactNumberId)
    {
        Query.Where(n => n.ContactNumberId == contactNumberId && n.Status == "scheduled" && n.MessageSid != null);
    }
}

public class NotificationsInRangeSpecification : Specification<OrderNotification>
{
    public NotificationsInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.CreatedAt >= from && n.CreatedAt < to);
    }
}
