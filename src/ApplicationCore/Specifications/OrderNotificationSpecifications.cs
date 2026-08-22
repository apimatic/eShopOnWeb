using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationsByOrderIdSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrderIdSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
            .OrderBy(n => n.CreatedAt);
    }
}

public class OrderNotificationsByOrderIdsSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrderIdsSpecification(IEnumerable<int> orderIds)
    {
        var ids = orderIds.ToList();
        Query.Where(n => ids.Contains(n.OrderId));
    }
}

public class PendingFollowUpByOrderSpecification : Specification<OrderNotification>
{
    public PendingFollowUpByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.Kind == NotificationKind.DeliveryFollowUp
            && n.ProviderMessageSid != null);
    }
}

public class NotificationsByContactNumberSpecification : Specification<OrderNotification>
{
    public NotificationsByContactNumberSpecification(int contactNumberId)
    {
        Query.Where(n => n.ContactNumberId == contactNumberId);
    }
}

public class ResendIdempotencySpecification : Specification<ResendIdempotencyRecord>
{
    public ResendIdempotencySpecification(int originalNotificationId, string idempotencyKey)
    {
        Query.Where(r => r.OriginalNotificationId == originalNotificationId && r.IdempotencyKey == idempotencyKey);
    }
}

public class OrderNotificationsInCreatedRangeSpecification : Specification<OrderNotification>
{
    public OrderNotificationsInCreatedRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.CreatedAt >= from && n.CreatedAt <= to);
    }
}

public class OrderNotificationsWithProviderSidSpecification : Specification<OrderNotification>
{
    public OrderNotificationsWithProviderSidSpecification()
    {
        Query.Where(n => n.ProviderMessageSid != null);
    }
}
