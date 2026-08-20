using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationsByOrderSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId).OrderBy(n => n.Id);
    }
}

public class OrderNotificationsByBuyerSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByBuyerSpecification(string buyerId)
    {
        Query.Where(n => n.BuyerId == buyerId);
    }
}

public class OrderNotificationByIdempotencySpecification : Specification<OrderNotification>
{
    public OrderNotificationByIdempotencySpecification(int parentNotificationId, string idempotencyKey)
    {
        Query.Where(n => n.ParentNotificationId == parentNotificationId && n.IdempotencyKey == idempotencyKey);
    }
}

public class OrderNotificationsInRangeSpecification : Specification<OrderNotification>
{
    public OrderNotificationsInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.DateCreated >= from && n.DateCreated <= to);
    }
}

public class PendingFollowUpNotificationsSpecification : Specification<OrderNotification>
{
    public PendingFollowUpNotificationsSpecification(int orderId)
    {
        Query.Where(n =>
            n.OrderId == orderId &&
            n.Kind == OrderNotificationKind.DeliveryFollowUp &&
            n.ProviderSid != null &&
            n.ProviderStatus != "canceled" &&
            n.ProviderStatus != "sent" &&
            n.ProviderStatus != "delivered" &&
            n.ProviderStatus != "undelivered" &&
            n.ProviderStatus != "failed");
    }
}
