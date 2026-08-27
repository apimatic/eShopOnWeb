using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationsByOrderSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
            .OrderBy(n => n.CreatedAt);
    }
}

public class ScheduledFollowUpsByOrderSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.Type == NotificationType.DeliveryFollowUp
            && n.Status == OrderNotificationStatuses.Scheduled
            && n.ProviderMessageSid != null);
    }
}

public class ScheduledNotificationsByContactNumberSpecification : Specification<OrderNotification>
{
    public ScheduledNotificationsByContactNumberSpecification(int contactNumberId)
    {
        Query.Where(n => n.ContactNumberId == contactNumberId
            && n.Status == OrderNotificationStatuses.Scheduled
            && n.ProviderMessageSid != null);
    }
}

public class OrderNotificationByResendKeySpecification : Specification<OrderNotification>
{
    public OrderNotificationByResendKeySpecification(string resendIdempotencyKey)
    {
        Query.Where(n => n.ResendIdempotencyKey == resendIdempotencyKey);
    }
}

public class OrderNotificationsCreatedInRangeSpecification : Specification<OrderNotification>
{
    public OrderNotificationsCreatedInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.CreatedAt >= from && n.CreatedAt <= to && n.ProviderMessageSid != null);
    }
}
