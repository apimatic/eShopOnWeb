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
        Query.Where(n => n.OrderId == orderId)
            .OrderBy(n => n.Id);
    }
}

public class OrderNotificationsByOrderIdsSpec : Specification<OrderNotification>
{
    public OrderNotificationsByOrderIdsSpec(IEnumerable<int> orderIds)
    {
        var ids = orderIds.ToList();
        Query.Where(n => ids.Contains(n.OrderId))
            .OrderBy(n => n.Id);
    }
}

public class PendingFollowUpsByOrderIdSpec : Specification<OrderNotification>
{
    public PendingFollowUpsByOrderIdSpec(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
                         && n.Kind == NotificationKind.DeliveryFollowUp
                         && n.ProviderMessageSid != null);
    }
}

public class PendingNotificationsByContactNumberIdSpec : Specification<OrderNotification>
{
    public PendingNotificationsByContactNumberIdSpec(int contactNumberId)
    {
        Query.Where(n => n.ContactNumberId == contactNumberId
                         && n.ProviderMessageSid != null);
    }
}

public class NotificationByResendIdempotencySpec : Specification<OrderNotification>, ISingleResultSpecification<OrderNotification>
{
    public NotificationByResendIdempotencySpec(int originalNotificationId, string idempotencyKey)
    {
        Query.Where(n => n.ResentFromNotificationId == originalNotificationId
                         && n.IdempotencyKey == idempotencyKey);
    }
}

public class NotificationsCreatedBetweenSpec : Specification<OrderNotification>
{
    public NotificationsCreatedBetweenSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.CreatedAt >= from && n.CreatedAt <= to)
            .OrderBy(n => n.CreatedAt);
    }
}
