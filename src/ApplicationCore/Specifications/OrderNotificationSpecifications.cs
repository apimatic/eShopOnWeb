using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

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

public class ScheduledFollowUpNotificationsSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpNotificationsSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
                         && n.Kind == OrderNotificationKind.DeliveryFollowUp
                         && n.ProviderMessageSid != null);
    }
}

public class ResendByIdempotencySpecification : Specification<OrderNotification>
{
    public ResendByIdempotencySpecification(int sourceNotificationId, string idempotencyKey)
    {
        Query.Where(n => n.ResendOfNotificationId == sourceNotificationId
                         && n.IdempotencyKey == idempotencyKey);
    }
}

public class OrderNotificationsInRangeSpecification : Specification<OrderNotification>
{
    public OrderNotificationsInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => (n.CreatedAt >= from && n.CreatedAt <= to)
                         || (n.ScheduledSendAt != null && n.ScheduledSendAt >= from && n.ScheduledSendAt <= to));
    }
}

public class OrderNotificationByProviderSidSpecification : Specification<OrderNotification>
{
    public OrderNotificationByProviderSidSpecification(string providerMessageSid)
    {
        Query.Where(n => n.ProviderMessageSid == providerMessageSid);
    }
}
