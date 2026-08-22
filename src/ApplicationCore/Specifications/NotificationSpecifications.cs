using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsByIdSpec : Specification<OrderNotification>
{
    public NotificationsByIdSpec(int notificationId)
    {
        Query.Where(n => n.Id == notificationId);
    }
}

public class NotificationsByOrderIdSpec : Specification<OrderNotification>
{
    public NotificationsByOrderIdSpec(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
            .OrderBy(n => n.Id);
    }
}

public class NotificationsByOrderIdAndBuyerSpec : Specification<OrderNotification>
{
    public NotificationsByOrderIdAndBuyerSpec(int orderId, string buyerId)
    {
        Query.Where(n => n.OrderId == orderId && n.BuyerId == buyerId)
            .OrderBy(n => n.Id);
    }
}

public class NotificationsByBuyerAndOrdersSpec : Specification<OrderNotification>
{
    public NotificationsByBuyerAndOrdersSpec(string buyerId, IReadOnlyList<int> orderIds)
    {
        Query.Where(n => n.BuyerId == buyerId && orderIds.Contains(n.OrderId))
            .OrderBy(n => n.Id);
    }
}

public class ScheduledFollowUpsByOrderSpec : Specification<OrderNotification>
{
    public ScheduledFollowUpsByOrderSpec(int orderId)
    {
        Query.Where(n => n.OrderId == orderId && n.Kind == NotificationKind.DeliveryFollowUp);
    }
}

public class ScheduledFollowUpsByDestinationSpec : Specification<OrderNotification>
{
    public ScheduledFollowUpsByDestinationSpec(string buyerId, string destinationCanonical)
    {
        Query.Where(n => n.BuyerId == buyerId
                         && n.DestinationCanonical == destinationCanonical
                         && n.Kind == NotificationKind.DeliveryFollowUp);
    }
}

public class ResendByParentAndKeySpec : Specification<OrderNotification>
{
    public ResendByParentAndKeySpec(int parentNotificationId, string idempotencyKey)
    {
        Query.Where(n => n.ParentNotificationId == parentNotificationId && n.IdempotencyKey == idempotencyKey);
    }
}

public class NotificationsInCreatedRangeSpec : Specification<OrderNotification>
{
    public NotificationsInCreatedRangeSpec(System.DateTimeOffset fromUtc, System.DateTimeOffset toUtc)
    {
        Query.Where(n => n.CreatedAt >= fromUtc && n.CreatedAt <= toUtc);
    }
}
