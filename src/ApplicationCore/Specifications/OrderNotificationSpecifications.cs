using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationsByOrderSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
            .OrderBy(n => n.Id);
    }
}

public class OrderNotificationsByBuyerSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByBuyerSpecification(string buyerId)
    {
        Query.Where(n => n.BuyerId == buyerId)
            .OrderBy(n => n.Id);
    }
}

public class PendingFollowUpNotificationsSpecification : Specification<OrderNotification>
{
    public PendingFollowUpNotificationsSpecification(int orderId)
    {
        Query.Where(n =>
            n.OrderId == orderId &&
            n.Kind == OrderNotificationKind.DeliveryFollowUp &&
            n.ProviderMessageSid != null &&
            (n.ProviderStatus == "scheduled" || n.ProviderStatus == "accepted" || n.ProviderStatus == "queued"));
    }
}

public class NotificationResendByKeySpecification : Specification<NotificationResendRecord>
{
    public NotificationResendByKeySpecification(int sourceNotificationId, string idempotencyKey)
    {
        Query.Where(r => r.SourceNotificationId == sourceNotificationId && r.IdempotencyKey == idempotencyKey);
    }
}

public class OrderNotificationsInRangeSpecification : Specification<OrderNotification>
{
    public OrderNotificationsInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.CreatedAt >= from && n.CreatedAt <= to);
    }
}

