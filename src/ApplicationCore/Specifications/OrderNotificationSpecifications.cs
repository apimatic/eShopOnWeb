using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All notifications for one order, oldest first.</summary>
public sealed class NotificationsByOrderSpecification : Specification<OrderNotification>
{
    public NotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
             .OrderBy(n => n.CreatedAt);
    }
}

/// <summary>A single notification by its id.</summary>
public sealed class NotificationByIdSpecification : Specification<OrderNotification>
{
    public NotificationByIdSpecification(int notificationId)
    {
        Query.Where(n => n.Id == notificationId);
    }
}

/// <summary>Notification(s) previously produced under a given idempotency key.</summary>
public sealed class NotificationByIdempotencyKeySpecification : Specification<OrderNotification>
{
    public NotificationByIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}

/// <summary>Notifications created within a date range (what eShop believes it sent).</summary>
public sealed class NotificationsCreatedBetweenSpecification : Specification<OrderNotification>
{
    public NotificationsCreatedBetweenSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.CreatedAt >= from && n.CreatedAt <= to);
    }
}

/// <summary>
/// Not-yet-sent scheduled follow-ups for an order — the messages that must be called off when
/// the order is cancelled.
/// </summary>
public sealed class PendingScheduledNotificationsForOrderSpecification : Specification<OrderNotification>
{
    public PendingScheduledNotificationsForOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
                         && n.IsScheduledFollowUp
                         && n.ProviderMessageSid != null
                         && n.Status == NotificationStatus.Scheduled);
    }
}

/// <summary>
/// Not-yet-sent scheduled messages aimed at a specific number for a shopper — called off when
/// that number is removed so nothing further reaches it.
/// </summary>
public sealed class PendingScheduledNotificationsForNumberSpecification : Specification<OrderNotification>
{
    public PendingScheduledNotificationsForNumberSpecification(string buyerId, string toNumber)
    {
        Query.Where(n => n.BuyerId == buyerId
                         && n.ToNumber == toNumber
                         && n.IsScheduledFollowUp
                         && n.ProviderMessageSid != null
                         && n.Status == NotificationStatus.Scheduled);
    }
}
