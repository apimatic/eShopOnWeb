using System.Collections.Generic;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class OrderNotificationsByOrderSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
            .OrderBy(n => n.Id);
    }
}

public sealed class OrderNotificationsByBuyerSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByBuyerSpecification(string buyerId)
    {
        Query.Where(n => n.BuyerId == buyerId)
            .OrderBy(n => n.OrderId).ThenBy(n => n.Id);
    }
}

public sealed class OrderNotificationByIdempotencyKeySpecification : Specification<OrderNotification>
{
    public OrderNotificationByIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}

public sealed class ScheduledFollowUpForOrderSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpForOrderSpecification(int orderId)
    {
        // A not-yet-sent scheduled follow-up: it has a provider SID and is still in a pre-send
        // state, whatever exact status string the provider used when it accepted the schedule.
        Query.Where(n => n.OrderId == orderId
            && n.Kind == NotificationKind.DeliveryFollowUp
            && n.ProviderMessageSid != null
            && (n.DeliveryStatus == NotificationDeliveryStatus.Scheduled
                || n.DeliveryStatus == NotificationDeliveryStatus.Queued
                || n.DeliveryStatus == NotificationDeliveryStatus.Unknown));
    }
}

public sealed class OrderNotificationsCreatedBetweenSpecification : Specification<OrderNotification>
{
    public OrderNotificationsCreatedBetweenSpecification(System.DateTimeOffset from, System.DateTimeOffset to)
    {
        Query.Where(n => n.ProviderMessageSid != null && n.CreatedAt >= from && n.CreatedAt <= to);
    }
}
