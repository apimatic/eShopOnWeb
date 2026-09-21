using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All notifications for one order, oldest first.</summary>
public sealed class OrderNotificationsByOrderSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
             .OrderBy(n => n.CreatedAt);
    }
}

/// <summary>A single notification by its id.</summary>
public sealed class OrderNotificationByIdSpecification : Specification<OrderNotification>
{
    public OrderNotificationByIdSpecification(int notificationId)
    {
        Query.Where(n => n.Id == notificationId);
    }
}

/// <summary>A prior re-send that already used a given caller idempotency key, if any.</summary>
public sealed class OrderNotificationByIdempotencyKeySpecification : Specification<OrderNotification>
{
    public OrderNotificationByIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}

/// <summary>The not-yet-sent (scheduled) follow-ups for an order that can still be called off.</summary>
public sealed class PendingFollowUpsByOrderSpecification : Specification<OrderNotification>
{
    public PendingFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
                         && n.IsScheduled
                         && n.MessageSid != null);
    }
}

/// <summary>Every notification eShop believes it handed to the provider (has a message SID), for reconciliation.</summary>
public sealed class SentOrderNotificationsSpecification : Specification<OrderNotification>
{
    public SentOrderNotificationsSpecification()
    {
        Query.Where(n => n.MessageSid != null);
    }
}
