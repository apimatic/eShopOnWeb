using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsByOrderIdSpecification : Specification<OrderNotification>
{
    public NotificationsByOrderIdSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
            .OrderBy(n => n.Id);
    }
}

public class NotificationsByOrderIdsSpecification : Specification<OrderNotification>
{
    public NotificationsByOrderIdsSpecification(IEnumerable<int> orderIds)
    {
        var ids = orderIds.ToArray();
        Query.Where(n => ids.Contains(n.OrderId))
            .OrderBy(n => n.Id);
    }
}

public class ScheduledFollowUpsByOrderSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n =>
            n.OrderId == orderId
            && n.Kind == OrderNotificationKinds.DeliveryFollowUp
            && n.ProviderStatus == "scheduled"
            && n.ProviderMessageSid != null);
    }
}

public class NotificationByResendIdempotencySpecification : Specification<OrderNotification>, ISingleResultSpecification<OrderNotification>
{
    public NotificationByResendIdempotencySpecification(int sourceNotificationId, string idempotencyKey)
    {
        Query.Where(n => n.SourceNotificationId == sourceNotificationId && n.IdempotencyKey == idempotencyKey);
    }
}

public class NotificationsInCreatedRangeSpecification : Specification<OrderNotification>
{
    public NotificationsInCreatedRangeSpecification(System.DateTimeOffset from, System.DateTimeOffset to)
    {
        Query.Where(n => n.CreatedAt >= from && n.CreatedAt <= to);
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
