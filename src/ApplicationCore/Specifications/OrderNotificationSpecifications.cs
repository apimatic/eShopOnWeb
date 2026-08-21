using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationsByOrderIdSpec : Specification<OrderNotification>
{
    public OrderNotificationsByOrderIdSpec(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
            .OrderBy(n => n.Id);
    }
}

public class OrderNotificationsByBuyerSpec : Specification<OrderNotification>
{
    public OrderNotificationsByBuyerSpec(string buyerId)
    {
        Query.Where(n => n.BuyerId == buyerId)
            .OrderBy(n => n.Id);
    }
}

public class OrderNotificationsByIdsSpec : Specification<OrderNotification>
{
    public OrderNotificationsByIdsSpec(IEnumerable<int> orderIds)
    {
        Query.Where(n => orderIds.Contains(n.OrderId));
    }
}

public class FollowUpNotificationsByOrderSpec : Specification<OrderNotification>
{
    public FollowUpNotificationsByOrderSpec(int orderId)
    {
        Query.Where(n => n.OrderId == orderId && n.Kind == NotificationKind.DeliveryFollowUp);
    }
}

public class ResendIdempotencyByKeySpec : Specification<ResendIdempotencyRecord>, ISingleResultSpecification<ResendIdempotencyRecord>
{
    public ResendIdempotencyByKeySpec(int sourceNotificationId, string idempotencyKey)
    {
        Query.Where(r => r.SourceNotificationId == sourceNotificationId && r.IdempotencyKey == idempotencyKey);
    }
}

public class OrderNotificationsWithProviderSidSpec : Specification<OrderNotification>
{
    public OrderNotificationsWithProviderSidSpec()
    {
        Query.Where(n => n.ProviderMessageSid != null);
    }
}
