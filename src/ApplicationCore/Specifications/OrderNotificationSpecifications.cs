using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationsByOrderIdSpec : Specification<OrderNotification>
{
    public OrderNotificationsByOrderIdSpec(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
            .OrderBy(n => n.Id);
    }
}

public class FollowUpsByOrderIdSpec : Specification<OrderNotification>
{
    public FollowUpsByOrderIdSpec(int orderId)
    {
        Query.Where(n => n.OrderId == orderId && n.Kind == NotificationKind.DeliveryFollowUp);
    }
}

public class FollowUpsByBuyerAndNumberSpec : Specification<OrderNotification>
{
    public FollowUpsByBuyerAndNumberSpec(string buyerId, string canonicalNumber)
    {
        Query.Where(n => n.BuyerId == buyerId
            && n.DestinationNumber == canonicalNumber
            && n.Kind == NotificationKind.DeliveryFollowUp);
    }
}

public class NotificationsWithProviderSidSpec : Specification<OrderNotification>
{
    public NotificationsWithProviderSidSpec()
    {
        Query.Where(n => n.ProviderSid != null);
    }
}

public class NotificationsCreatedBetweenSpec : Specification<OrderNotification>
{
    public NotificationsCreatedBetweenSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.CreatedAt >= from && n.CreatedAt <= to);
    }
}

public class ResendIdempotencySpec : Specification<ResendIdempotencyRecord>, ISingleResultSpecification
{
    public ResendIdempotencySpec(int sourceNotificationId, string idempotencyKey)
    {
        Query.Where(r => r.SourceNotificationId == sourceNotificationId && r.IdempotencyKey == idempotencyKey);
    }
}
