using System;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All notifications for one order, oldest first.</summary>
public sealed class NotificationsByOrderSpecification : Specification<Notification>
{
    public NotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
             .OrderBy(n => n.CreatedAt);
    }
}

/// <summary>All notifications for a set of orders (used to attach notification state to my-orders).</summary>
public sealed class NotificationsByOrderIdsSpecification : Specification<Notification>
{
    public NotificationsByOrderIdsSpecification(int[] orderIds)
    {
        Query.Where(n => orderIds.Contains(n.OrderId))
             .OrderBy(n => n.CreatedAt);
    }
}

/// <summary>A single notification by id.</summary>
public sealed class NotificationByIdSpecification : Specification<Notification>
{
    public NotificationByIdSpecification(int notificationId)
    {
        Query.Where(n => n.Id == notificationId);
    }
}

/// <summary>
/// The not-yet-sent scheduled follow-up for an order, if one exists (used to call it off on cancel).
/// </summary>
public sealed class ScheduledFollowUpForOrderSpecification : Specification<Notification>
{
    public ScheduledFollowUpForOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
                         && n.IsScheduledFollowUp
                         && n.Status == NotificationStatus.Scheduled);
    }
}

/// <summary>An existing notification produced under a given operator idempotency key, if any.</summary>
public sealed class NotificationByIdempotencyKeySpecification : Specification<Notification>
{
    public NotificationByIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}

/// <summary>
/// Notifications this app believes it sent within a window — those that reached the provider and
/// carry its identifier. Used as the eShop side of the reconciliation report.
/// </summary>
public sealed class SentNotificationsBetweenSpecification : Specification<Notification>
{
    public SentNotificationsBetweenSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.ProviderMessageId != null
                         && n.CreatedAt >= from
                         && n.CreatedAt <= to);
    }
}
