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
        Query.Where(n => ids.Contains(n.OrderId))
            .OrderBy(n => n.CreatedAt);
    }
}

public class OrderNotificationsByCreatedRangeSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByCreatedRangeSpecification(System.DateTimeOffset from, System.DateTimeOffset to)
    {
        Query.Where(n => n.CreatedAt >= from && n.CreatedAt <= to);
    }
}

public class CancellableFollowUpsByOrderSpecification : Specification<OrderNotification>
{
    public CancellableFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.Kind == OrderNotificationKind.DeliveryFollowUp
            && n.ProviderMessageSid != null);
    }
}

public class NotificationResendByKeySpecification : Specification<NotificationResendRecord>
{
    public NotificationResendByKeySpecification(int sourceNotificationId, string idempotencyKey)
    {
        Query.Where(r => r.SourceNotificationId == sourceNotificationId && r.IdempotencyKey == idempotencyKey);
    }
}
