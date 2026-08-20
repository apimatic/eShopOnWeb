using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationsByOrderIdSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrderIdSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
            .OrderBy(n => n.Id);
    }
}

public class OrderNotificationsByOrderIdsSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrderIdsSpecification(IReadOnlyCollection<int> orderIds)
    {
        Query.Where(n => orderIds.Contains(n.OrderId))
            .OrderBy(n => n.Id);
    }
}

public class ScheduledFollowUpsByOrderSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
                         && n.Kind == OrderNotificationKind.DeliveryFollowUp
                         && n.ProviderMessageSid != null);
    }
}

public class ScheduledFollowUpsByContactNumberSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpsByContactNumberSpecification(int contactNumberId)
    {
        Query.Where(n => n.ContactNumberId == contactNumberId
                         && n.Kind == OrderNotificationKind.DeliveryFollowUp
                         && n.ProviderMessageSid != null);
    }
}

public class OrderNotificationsByProviderSidsSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByProviderSidsSpecification(IReadOnlyCollection<string> sids)
    {
        Query.Where(n => n.ProviderMessageSid != null && sids.Contains(n.ProviderMessageSid));
    }
}

public class OrderNotificationsWithProviderSidInPeriodSpecification : Specification<OrderNotification>
{
    public OrderNotificationsWithProviderSidInPeriodSpecification(System.DateTimeOffset from, System.DateTimeOffset to)
    {
        Query.Where(n => n.ProviderMessageSid != null && n.CreatedAt >= from && n.CreatedAt <= to);
    }
}

public class NotificationResendByKeySpecification : Specification<NotificationResendRecord>
{
    public NotificationResendByKeySpecification(int sourceNotificationId, string idempotencyKey)
    {
        Query.Where(r => r.SourceNotificationId == sourceNotificationId && r.IdempotencyKey == idempotencyKey);
    }
}
