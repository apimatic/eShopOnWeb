using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsByOrderIdSpec : Specification<OrderNotification>
{
    public NotificationsByOrderIdSpec(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
            .OrderBy(n => n.CreatedAt);
    }
}

public class NotificationsByOrderIdsSpec : Specification<OrderNotification>
{
    public NotificationsByOrderIdsSpec(IEnumerable<int> orderIds)
    {
        var ids = orderIds.ToArray();
        Query.Where(n => ids.Contains(n.OrderId));
    }
}

public class FollowUpNotificationsByOrderIdSpec : Specification<OrderNotification>
{
    public FollowUpNotificationsByOrderIdSpec(int orderId)
    {
        Query.Where(n => n.OrderId == orderId && n.Kind == NotificationKinds.DeliveryFollowUp);
    }
}

public class ResendRecordByKeySpec : Specification<NotificationResendRecord>
{
    public ResendRecordByKeySpec(int sourceNotificationId, string idempotencyKey)
    {
        Query.Where(r => r.SourceNotificationId == sourceNotificationId && r.IdempotencyKey == idempotencyKey);
    }
}

public class NotificationsInRangeSpec : Specification<OrderNotification>
{
    public NotificationsInRangeSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.CreatedAt >= from && n.CreatedAt <= to && n.ProviderSid != null);
    }
}

public class NotificationsByProviderSidsSpec : Specification<OrderNotification>
{
    public NotificationsByProviderSidsSpec(IEnumerable<string> sids)
    {
        var sidSet = sids.ToArray();
        Query.Where(n => n.ProviderSid != null && sidSet.Contains(n.ProviderSid));
    }
}
