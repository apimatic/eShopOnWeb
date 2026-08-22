using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsByOrderIdSpec : Specification<OrderNotification>
{
    public NotificationsByOrderIdSpec(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
            .OrderBy(n => n.CreatedAt);
    }
}

public class NotificationsByBuyerSpec : Specification<OrderNotification>
{
    public NotificationsByBuyerSpec(string buyerId)
    {
        Query.Where(n => n.BuyerId == buyerId)
            .OrderBy(n => n.CreatedAt);
    }
}

public class NotificationsByIdsSpec : Specification<OrderNotification>
{
    public NotificationsByIdsSpec(params int[] orderIds)
    {
        Query.Where(n => orderIds.Contains(n.OrderId))
            .OrderBy(n => n.CreatedAt);
    }
}

public class ScheduledNotificationsByContactSpec : Specification<OrderNotification>
{
    public ScheduledNotificationsByContactSpec(int contactNumberId)
    {
        Query.Where(n => n.ContactNumberId == contactNumberId
                         && n.ProviderMessageSid != null
                         && (n.ProviderStatus == "scheduled" || n.ScheduledFor != null && n.ProviderStatus != "canceled" && n.ProviderStatus != "delivered" && n.ProviderStatus != "sent" && n.ProviderStatus != "undelivered" && n.ProviderStatus != "failed"));
    }
}

public class ScheduledFollowUpsByOrderSpec : Specification<OrderNotification>
{
    public ScheduledFollowUpsByOrderSpec(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
                         && n.Kind == NotificationKind.DeliveryFollowUp
                         && n.ProviderMessageSid != null
                         && n.ProviderStatus != "canceled"
                         && n.ProviderStatus != "sent"
                         && n.ProviderStatus != "delivered"
                         && n.ProviderStatus != "undelivered"
                         && n.ProviderStatus != "failed");
    }
}

public class ResendByIdempotencySpec : Specification<OrderNotification>, ISingleResultSpecification<OrderNotification>
{
    public ResendByIdempotencySpec(int sourceNotificationId, string idempotencyKey)
    {
        Query.Where(n => n.ResendOfNotificationId == sourceNotificationId && n.IdempotencyKey == idempotencyKey);
    }
}

public class NotificationsInCreatedRangeSpec : Specification<OrderNotification>
{
    public NotificationsInCreatedRangeSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.CreatedAt >= from && n.CreatedAt <= to);
    }
}

public class NotificationsByProviderSidsSpec : Specification<OrderNotification>
{
    public NotificationsByProviderSidsSpec(IEnumerable<string> sids)
    {
        var sidList = sids.ToList();
        Query.Where(n => n.ProviderMessageSid != null && sidList.Contains(n.ProviderMessageSid));
    }
}
