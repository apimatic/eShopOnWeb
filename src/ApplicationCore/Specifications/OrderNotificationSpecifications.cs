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

public class ScheduledFollowUpsByOrderSpec : Specification<OrderNotification>
{
    public ScheduledFollowUpsByOrderSpec(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
                         && n.Kind == NotificationKind.DeliveryFollowUp
                         && n.ProviderSid != null
                         && n.Status == "scheduled");
    }
}

public class ResendIdempotencyByKeySpec : Specification<ResendIdempotencyRecord>
{
    public ResendIdempotencyByKeySpec(string idempotencyKey, int sourceNotificationId)
    {
        Query.Where(r => r.IdempotencyKey == idempotencyKey && r.SourceNotificationId == sourceNotificationId);
    }
}

public class NotificationsByProviderSidsSpec : Specification<OrderNotification>
{
    public NotificationsByProviderSidsSpec(IReadOnlyList<string> sids)
    {
        Query.Where(n => n.ProviderSid != null && sids.Contains(n.ProviderSid));
    }
}

public class NotificationsWithProviderSidInRangeSpec : Specification<OrderNotification>
{
    public NotificationsWithProviderSidInRangeSpec(System.DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.CreatedAt >= from && n.CreatedAt <= to && n.ProviderSid != null);
    }
}
