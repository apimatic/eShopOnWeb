using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationsByOrderIdSpec : Specification<OrderNotification>
{
    public OrderNotificationsByOrderIdSpec(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
            .OrderBy(n => n.CreatedAt);
    }
}

public class OrderNotificationsByBuyerIdSpec : Specification<OrderNotification>
{
    public OrderNotificationsByBuyerIdSpec(string buyerId)
    {
        Query.Where(n => n.BuyerId == buyerId);
    }
}

public class PendingFollowUpsByOrderIdSpec : Specification<OrderNotification>
{
    public PendingFollowUpsByOrderIdSpec(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.Kind == NotificationKind.DeliveryFollowUp
            && n.ProviderMessageSid != null
            && (n.ProviderStatus == "scheduled"
                || n.ProviderStatus == "queued"
                || n.ProviderStatus == "accepted"
                || n.ProviderStatus == null));
    }
}

public class OrderNotificationsByCreatedRangeSpec : Specification<OrderNotification>
{
    public OrderNotificationsByCreatedRangeSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.CreatedAt >= from && n.CreatedAt <= to);
    }
}

public class OrderNotificationByProviderSidSpec : Specification<OrderNotification>
{
    public OrderNotificationByProviderSidSpec(string providerMessageSid)
    {
        Query.Where(n => n.ProviderMessageSid == providerMessageSid);
    }
}

public class NotificationResendByKeySpec : Specification<NotificationResend>
{
    public NotificationResendByKeySpec(int sourceNotificationId, string idempotencyKey)
    {
        Query.Where(r => r.SourceNotificationId == sourceNotificationId && r.IdempotencyKey == idempotencyKey);
    }
}
