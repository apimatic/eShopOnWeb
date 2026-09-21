using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All notifications produced for a single order, oldest first.</summary>
public sealed class OrderNotificationsByOrderSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
             .OrderBy(n => n.CreatedAt);
    }
}

/// <summary>All notifications belonging to a shopper (for the my-orders view).</summary>
public sealed class OrderNotificationsByBuyerSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByBuyerSpecification(string buyerId)
    {
        Query.Where(n => n.BuyerId == buyerId)
             .OrderBy(n => n.CreatedAt);
    }
}

/// <summary>Not-yet-sent scheduled follow-ups for an order that can still be called off.</summary>
public sealed class ScheduledFollowUpsForOrderSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpsForOrderSpecification(int orderId)
    {
        Query.Where(n =>
            n.OrderId == orderId &&
            n.Kind == NotificationKind.DeliveryFollowUp &&
            n.ProviderMessageSid != null &&
            n.Status == "scheduled");
    }
}

/// <summary>A notification created under a given resend idempotency key, if one exists.</summary>
public sealed class OrderNotificationByIdempotencyKeySpecification : Specification<OrderNotification>
{
    public OrderNotificationByIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}

/// <summary>Notifications this app recorded with a provider SID whose send falls within a range.</summary>
public sealed class OrderNotificationsWithSidInRangeSpecification : Specification<OrderNotification>
{
    public OrderNotificationsWithSidInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n =>
            n.ProviderMessageSid != null &&
            n.CreatedAt >= from &&
            n.CreatedAt <= to);
    }
}
