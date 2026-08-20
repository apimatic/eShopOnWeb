using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationsByOrderIdSpec : Specification<OrderNotification>
{
    public OrderNotificationsByOrderIdSpec(int orderId)
    {
        Query.Where(n => n.ForOrderId == orderId)
            .OrderBy(n => n.Id);
    }
}

public class OrderNotificationsByOrderIdsSpec : Specification<OrderNotification>
{
    public OrderNotificationsByOrderIdsSpec(IEnumerable<int> orderIds)
    {
        var ids = orderIds.ToArray();
        Query.Where(n => ids.Contains(n.ForOrderId))
            .OrderBy(n => n.Id);
    }
}

public class ScheduledFollowUpsByOrderSpec : Specification<OrderNotification>
{
    public ScheduledFollowUpsByOrderSpec(int orderId)
    {
        Query.Where(n =>
            n.ForOrderId == orderId
            && n.Type == NotificationType.DeliveryFollowUp
            && n.ProviderStatus == "scheduled"
            && n.ProviderMessageSid != null);
    }
}

public class ResendByIdempotencySpec : Specification<OrderNotification>
{
    public ResendByIdempotencySpec(int sourceNotificationId, string idempotencyKey)
    {
        Query.Where(n =>
            n.ResentFromNotificationId == sourceNotificationId
            && n.ResendIdempotencyKey == idempotencyKey);
    }
}

public class OrderNotificationsByProviderSidsSpec : Specification<OrderNotification>
{
    public OrderNotificationsByProviderSidsSpec(IEnumerable<string> sids)
    {
        var sidList = sids.ToArray();
        Query.Where(n => n.ProviderMessageSid != null && sidList.Contains(n.ProviderMessageSid));
    }
}

public class OrderNotificationsByCreatedRangeSpec : Specification<OrderNotification>
{
    public OrderNotificationsByCreatedRangeSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.CreatedAt >= from && n.CreatedAt <= to);
    }
}
