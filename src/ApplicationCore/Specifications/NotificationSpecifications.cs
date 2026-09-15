using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsByOrderIdSpec : Specification<OrderNotification>
{
    public NotificationsByOrderIdSpec(int orderId)
    {
        Query.Where(notification => notification.OrderId == orderId)
            .OrderBy(notification => notification.Id);
    }
}

public class NotificationsByOrderIdsSpec : Specification<OrderNotification>
{
    public NotificationsByOrderIdsSpec(IReadOnlyCollection<int> orderIds)
    {
        Query.Where(notification => orderIds.Contains(notification.OrderId))
            .OrderBy(notification => notification.Id);
    }
}

public class ScheduledFollowUpsByOrderIdSpec : Specification<OrderNotification>
{
    public ScheduledFollowUpsByOrderIdSpec(int orderId)
    {
        Query.Where(notification =>
            notification.OrderId == orderId &&
            notification.Kind == NotificationKind.DeliveryFollowUp &&
            notification.ProviderMessageSid != null);
    }
}

public class NotificationsCreatedInRangeSpec : Specification<OrderNotification>
{
    public NotificationsCreatedInRangeSpec(System.DateTimeOffset from, System.DateTimeOffset to)
    {
        Query.Where(notification => notification.CreatedAt >= from && notification.CreatedAt <= to);
    }
}

public class ResendRecordByKeySpec : Specification<NotificationResendRecord>, ISingleResultSpecification<NotificationResendRecord>
{
    public ResendRecordByKeySpec(int sourceNotificationId, string idempotencyKey)
    {
        Query.Where(record => record.SourceNotificationId == sourceNotificationId && record.IdempotencyKey == idempotencyKey);
    }
}
