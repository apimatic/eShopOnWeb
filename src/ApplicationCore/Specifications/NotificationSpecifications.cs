using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

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
    public NotificationsByOrderIdsSpecification(IReadOnlyCollection<int> orderIds)
    {
        Query.Where(n => orderIds.Contains(n.OrderId))
            .OrderBy(n => n.Id);
    }
}

public class NotificationByIdSpecification : Specification<OrderNotification>, ISingleResultSpecification<OrderNotification>
{
    public NotificationByIdSpecification(int notificationId)
    {
        Query.Where(n => n.Id == notificationId);
    }
}

public class NotificationsByProviderSidsSpecification : Specification<OrderNotification>
{
    public NotificationsByProviderSidsSpecification(IReadOnlyCollection<string> providerSids)
    {
        Query.Where(n => n.ProviderSid != null && providerSids.Contains(n.ProviderSid));
    }
}

public class NotificationsInCreatedRangeSpecification : Specification<OrderNotification>
{
    public NotificationsInCreatedRangeSpecification(System.DateTimeOffset from, System.DateTimeOffset to)
    {
        Query.Where(n => n.CreatedAt >= from && n.CreatedAt <= to);
    }
}

public class PendingFollowUpsByOrderSpecification : Specification<OrderNotification>
{
    public PendingFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n =>
                n.OrderId == orderId &&
                n.Kind == NotificationKind.DeliveryFollowUp &&
                n.ProviderSid != null)
            .OrderBy(n => n.Id);
    }
}

public class ResendRecordBySourceAndKeySpecification : Specification<NotificationResendRecord>, ISingleResultSpecification<NotificationResendRecord>
{
    public ResendRecordBySourceAndKeySpecification(int sourceNotificationId, string idempotencyKey)
    {
        Query.Where(r => r.SourceNotificationId == sourceNotificationId && r.IdempotencyKey == idempotencyKey);
    }
}
