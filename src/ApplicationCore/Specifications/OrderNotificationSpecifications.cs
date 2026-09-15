using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>The progress record for a single order.</summary>
public class OrderProgressByOrderIdSpecification : Specification<OrderProgress>
{
    public OrderProgressByOrderIdSpecification(int orderId)
    {
        Query.Where(p => p.OrderId == orderId);
    }
}

/// <summary>The progress records for a set of orders (for building the caller's order list).</summary>
public class OrderProgressByOrderIdsSpecification : Specification<OrderProgress>
{
    public OrderProgressByOrderIdsSpecification(IEnumerable<int> orderIds)
    {
        Query.Where(p => orderIds.Contains(p.OrderId));
    }
}

/// <summary>
/// Notifications eShop believes it handed to the provider (they carry a provider message id) within a
/// date range. Used to line eShop's record up against the provider's during reconciliation.
/// </summary>
public class SentOrderNotificationsInRangeSpecification : Specification<OrderNotification>
{
    public SentOrderNotificationsInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        // "Believes it sent" means handed to the provider for delivery from our own number. A message
        // that is still scheduled, was cancelled before sending, or never reached the provider was not
        // actually sent, so it is excluded from the eShop side of the reconciliation.
        Query.Where(n => n.ProviderMessageSid != null
                         && n.Status != NotificationStatuses.Scheduled
                         && n.Status != NotificationStatuses.Canceled
                         && n.Status != NotificationStatuses.NotSent
                         && n.CreatedDate >= from
                         && n.CreatedDate <= to);
    }
}

/// <summary>All notifications for one order, oldest first.</summary>
public class OrderNotificationsByOrderIdSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrderIdSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
             .OrderBy(n => n.CreatedDate);
    }
}

/// <summary>All notifications across a set of orders (for building the caller's order list).</summary>
public class OrderNotificationsByOrderIdsSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrderIdsSpecification(IEnumerable<int> orderIds)
    {
        Query.Where(n => orderIds.Contains(n.OrderId))
             .OrderBy(n => n.CreatedDate);
    }
}

/// <summary>The not-yet-sent follow-up(s) queued for an order that must be called off on cancel.</summary>
public class ScheduledFollowUpsByOrderIdSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpsByOrderIdSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
                         && n.Type == OrderNotificationType.DeliveryFollowUp
                         && n.Status == NotificationStatuses.Scheduled
                         && n.ProviderMessageSid != null);
    }
}

/// <summary>A notification looked up by the idempotency key that produced it.</summary>
public class OrderNotificationByIdempotencyKeySpecification : Specification<OrderNotification>
{
    public OrderNotificationByIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}
