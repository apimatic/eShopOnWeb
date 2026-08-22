using System;
using System.Collections.Generic;
using System.Linq;
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

public class NotificationsByOrdersSpecification : Specification<OrderNotification>
{
    public NotificationsByOrdersSpecification(IEnumerable<int> orderIds)
    {
        var ids = orderIds.ToArray();
        Query.Where(n => ids.Contains(n.OrderId))
            .OrderBy(n => n.Id);
    }
}

public class NotificationByParentAndIdempotencyKeySpecification : Specification<OrderNotification>
{
    public NotificationByParentAndIdempotencyKeySpecification(int parentNotificationId, string idempotencyKey)
    {
        Query.Where(n => n.ParentNotificationId == parentNotificationId && n.IdempotencyKey == idempotencyKey);
    }
}

public class DeliveryFollowUpsByOrderSpecification : Specification<OrderNotification>
{
    public DeliveryFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId && n.Kind == NotificationKind.DeliveryFollowUp);
    }
}

public class NotificationsCreatedBetweenSpecification : Specification<OrderNotification>
{
    public NotificationsCreatedBetweenSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.CreatedUtc >= from && n.CreatedUtc <= to);
    }
}

public class NotificationsByProviderSidsSpecification : Specification<OrderNotification>
{
    public NotificationsByProviderSidsSpecification(IEnumerable<string> providerSids)
    {
        var sids = providerSids.ToArray();
        Query.Where(n => n.ProviderMessageSid != null && sids.Contains(n.ProviderMessageSid));
    }
}
