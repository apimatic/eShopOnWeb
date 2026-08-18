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
             .OrderBy(n => n.CreatedAt);
    }
}

/// <summary>Notifications for a set of orders (used to summarise a shopper's orders).</summary>
public class OrderNotificationsByOrderIdsSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrderIdsSpecification(int[] orderIds)
    {
        Query.Where(n => orderIds.Contains(n.OrderId))
             .OrderBy(n => n.CreatedAt);
    }
}

/// <summary>A single notification by its identifier.</summary>
public class OrderNotificationByIdSpecification : Specification<OrderNotification>
{
    public OrderNotificationByIdSpecification(int notificationId)
    {
        Query.Where(n => n.Id == notificationId);
    }
}

/// <summary>The (at most one) notification produced under a given resend idempotency key.</summary>
public class OrderNotificationByIdempotencyKeySpecification : Specification<OrderNotification>
{
    public OrderNotificationByIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}

/// <summary>Notifications that carry a provider message id (used for reconciliation).</summary>
public class OrderNotificationsWithProviderIdSpecification : Specification<OrderNotification>
{
    public OrderNotificationsWithProviderIdSpecification()
    {
        Query.Where(n => n.ProviderMessageSid != null && n.ProviderMessageSid != "");
    }
}
