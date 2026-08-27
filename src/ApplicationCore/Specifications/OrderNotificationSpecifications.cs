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

public class OrderNotificationsByBuyerIdSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByBuyerIdSpecification(string buyerId)
    {
        Query.Where(n => n.BuyerId == buyerId);
    }
}

public class OrderNotificationsByIdsSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByIdsSpecification(IEnumerable<int> orderIds)
    {
        var ids = orderIds.ToArray();
        Query.Where(n => ids.Contains(n.OrderId));
    }
}

public class ScheduledFollowUpNotificationsSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpNotificationsSpecification(int orderId)
    {
        Query.Where(n =>
            n.OrderId == orderId &&
            n.Kind == OrderNotificationKind.DeliveryFollowUp &&
            n.ProviderMessageSid != null &&
            n.ProviderStatus.ToLower() == "scheduled");
    }
}

public class NotificationResendKeySpecification : Specification<NotificationResendKey>, ISingleResultSpecification<NotificationResendKey>
{
    public NotificationResendKeySpecification(int sourceNotificationId, string idempotencyKey)
    {
        Query.Where(k => k.SourceNotificationId == sourceNotificationId && k.IdempotencyKey == idempotencyKey);
    }
}

public class OrderNotificationsWithProviderSidSpecification : Specification<OrderNotification>
{
    public OrderNotificationsWithProviderSidSpecification()
    {
        Query.Where(n => n.ProviderMessageSid != null);
    }
}
