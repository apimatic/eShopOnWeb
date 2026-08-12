using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All notifications raised for a given order, oldest first.</summary>
public class OrderNotificationsByOrderSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
            .OrderBy(n => n.CreatedDate);
    }
}

/// <summary>Notifications for a set of orders, used to project statuses onto a shopper's order list.</summary>
public class OrderNotificationsByOrderIdsSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrderIdsSpecification(int[] orderIds)
    {
        Query.Where(n => orderIds.Contains(n.OrderId))
            .OrderBy(n => n.CreatedDate);
    }
}

/// <summary>
/// The still-scheduled follow-up(s) for an order that have not yet gone out — the
/// messages that must be called off with the provider when an order is cancelled.
/// </summary>
public class PendingScheduledNotificationsByOrderSpecification : Specification<OrderNotification>
{
    public PendingScheduledNotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n =>
            n.OrderId == orderId &&
            n.IsScheduled &&
            n.ProviderMessageSid != null &&
            n.DeliveryStatus == MessageDeliveryStatus.Scheduled);
    }
}

/// <summary>A previously created resend keyed by the caller's idempotency key.</summary>
public class ResendByIdempotencyKeySpecification : Specification<OrderNotification>
{
    public ResendByIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}
