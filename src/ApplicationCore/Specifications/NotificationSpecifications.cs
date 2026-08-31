using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumbersByOwnerSpecification : Specification<ContactNumber>
{
    public ContactNumbersByOwnerSpecification(string ownerId)
    {
        Query.Where(c => c.OwnerId == ownerId);
    }
}

public class NotificationsByOrderSpecification : Specification<OrderNotification>
{
    public NotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
            .OrderBy(n => n.CreatedAt);
    }
}

public class NotificationByIdempotencyKeySpecification : Specification<OrderNotification>
{
    public NotificationByIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}

public class FollowUpNotificationsByOrderSpecification : Specification<OrderNotification>
{
    public FollowUpNotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId &&
                         n.Kind == NotificationKind.DeliveryFollowUp &&
                         n.ProviderMessageSid != null);
    }
}

public class PendingNotificationsByContactNumberSpecification : Specification<OrderNotification>
{
    public PendingNotificationsByContactNumberSpecification(int contactNumberId)
    {
        Query.Where(n => n.ContactNumberId == contactNumberId && n.ProviderMessageSid != null);
    }
}

public class NotificationsCreatedInRangeSpecification : Specification<OrderNotification>
{
    public NotificationsCreatedInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.CreatedAt >= from && n.CreatedAt <= to);
    }
}
