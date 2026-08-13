using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All notifications sent about a given order, newest first.</summary>
public class OrderNotificationsByOrderSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
             .OrderByDescending(n => n.CreatedAt);
    }
}

/// <summary>The scheduled follow-up(s) for an order that have not yet gone out and can still be called off.</summary>
public class ScheduledFollowUpsForOrderSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpsForOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
                         && n.Type == NotificationType.DeliveryFollowUp
                         && n.Status == NotificationStatus.Scheduled
                         && n.ProviderMessageSid != null);
    }
}

/// <summary>Locates a prior resend by its idempotency key so a repeat under the same key sends nothing new.</summary>
public class OrderNotificationByIdempotencyKeySpecification : Specification<OrderNotification>
{
    public OrderNotificationByIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}

/// <summary>
/// What eShop believes it submitted to the provider within a range: every notification that carries a
/// provider message id and was submitted between the bounds (inclusive).
/// </summary>
public class SubmittedNotificationsInRangeSpecification : Specification<OrderNotification>
{
    public SubmittedNotificationsInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.ProviderMessageSid != null
                         && n.SentAt != null
                         && n.SentAt >= from
                         && n.SentAt <= to);
    }
}
