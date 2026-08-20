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

public class NotificationsByBuyerSpecification : Specification<OrderNotification>
{
    public NotificationsByBuyerSpecification(string buyerId)
    {
        Query.Where(n => n.BuyerId == buyerId)
            .OrderBy(n => n.Id);
    }
}

public class NotificationsByOrdersSpecification : Specification<OrderNotification>
{
    public NotificationsByOrdersSpecification(IEnumerable<int> orderIds)
    {
        Query.Where(n => orderIds.Contains(n.OrderId))
            .OrderBy(n => n.Id);
    }
}

public class ScheduledFollowUpsByOrderSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
                         && n.Kind == OrderNotificationKind.DeliveryFollowUp
                         && n.ProviderSid != null
                         && (n.ProviderStatus == "scheduled" || n.ProviderStatus == "accepted"));
    }
}

public class NotificationsWithProviderSidSpecification : Specification<OrderNotification>
{
    public NotificationsWithProviderSidSpecification()
    {
        Query.Where(n => n.ProviderSid != null);
    }
}

public class ResendKeySpecification : Specification<NotificationResendKey>
{
    public ResendKeySpecification(int sourceNotificationId, string idempotencyKey)
    {
        Query.Where(k => k.SourceNotificationId == sourceNotificationId && k.IdempotencyKey == idempotencyKey);
    }
}
