using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All notifications for one order, newest first.</summary>
public class OrderNotificationsByOrderSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
            .OrderByDescending(n => n.CreatedAt);
    }
}

/// <summary>Notifications for a set of orders (for summarising a shopper's orders).</summary>
public class OrderNotificationsByOrderIdsSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrderIdsSpecification(int[] orderIds)
    {
        Query.Where(n => orderIds.Contains(n.OrderId));
    }
}

/// <summary>
/// The not-yet-sent scheduled follow-ups for an order — the messages a cancellation must call off
/// before they reach the shopper.
/// </summary>
public class PendingScheduledNotificationsForOrderSpecification : Specification<OrderNotification>
{
    public PendingScheduledNotificationsForOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.IsScheduled
            && n.ProviderMessageSid != null
            && n.ProviderStatus != "canceled"
            && n.ProviderStatus != "sent"
            && n.ProviderStatus != "delivered"
            && n.ProviderStatus != "failed"
            && n.ProviderStatus != "undelivered");
    }
}

/// <summary>Finds a prior resend recorded under the same caller idempotency key.</summary>
public class OrderNotificationByIdempotencyKeySpecification : Specification<OrderNotification>
{
    public OrderNotificationByIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}

/// <summary>Every notification eShop has a provider SID for — the set it believes it handed to the provider.</summary>
public class OrderNotificationsWithProviderSidSpecification : Specification<OrderNotification>
{
    public OrderNotificationsWithProviderSidSpecification()
    {
        Query.Where(n => n.ProviderMessageSid != null);
    }
}
