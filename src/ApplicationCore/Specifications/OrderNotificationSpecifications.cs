using System;
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

public class OrderNotificationsByIdsSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByIdsSpecification(IEnumerable<int> orderIds)
    {
        var ids = orderIds.ToArray();
        Query.Where(n => ids.Contains(n.OrderId))
            .OrderBy(n => n.Id);
    }
}

public class PendingFollowUpsByOrderSpecification : Specification<OrderNotification>
{
    public PendingFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
                && n.Kind == NotificationKind.DeliveryFollowUp
                && n.ProviderMessageSid != null);
    }
}

public class NotificationsToDestinationSpecification : Specification<OrderNotification>
{
    public NotificationsToDestinationSpecification(string destinationNumber)
    {
        Query.Where(n => n.DestinationNumber == destinationNumber
                && n.ProviderMessageSid != null
                && (n.ProviderStatus == "scheduled"
                    || n.ProviderStatus == "accepted"
                    || n.ProviderStatus == "queued"));
    }
}

public class OrderNotificationsInRangeSpecification : Specification<OrderNotification>
{
    public OrderNotificationsInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.CreatedAt >= from && n.CreatedAt <= to);
    }
}

public class OrderNotificationsByProviderSidsSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByProviderSidsSpecification(IEnumerable<string> providerSids)
    {
        var sids = providerSids.ToArray();
        Query.Where(n => n.ProviderMessageSid != null && sids.Contains(n.ProviderMessageSid));
    }
}

public class NotificationIdempotencySpecification : Specification<NotificationIdempotencyRecord>
{
    public NotificationIdempotencySpecification(int sourceNotificationId, string idempotencyKey)
    {
        Query.Where(r => r.SourceNotificationId == sourceNotificationId && r.IdempotencyKey == idempotencyKey);
    }
}
