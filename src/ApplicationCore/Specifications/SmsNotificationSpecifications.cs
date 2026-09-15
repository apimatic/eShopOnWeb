using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Every notification recorded for one order, oldest first.</summary>
public sealed class SmsNotificationsByOrderSpecification : Specification<SmsNotification>
{
    public SmsNotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
             .OrderBy(n => n.Id);
    }
}

/// <summary>Notifications for a set of orders (used to attach delivery state to a shopper's orders).</summary>
public sealed class SmsNotificationsByOrdersSpecification : Specification<SmsNotification>
{
    public SmsNotificationsByOrdersSpecification(IReadOnlyCollection<int> orderIds)
    {
        Query.Where(n => orderIds.Contains(n.OrderId))
             .OrderBy(n => n.Id);
    }
}

/// <summary>A single notification by its id.</summary>
public sealed class SmsNotificationByIdSpecification : Specification<SmsNotification>
{
    public SmsNotificationByIdSpecification(int notificationId)
    {
        Query.Where(n => n.Id == notificationId);
    }
}

/// <summary>The notification a prior resend produced under a given idempotency key, if any.</summary>
public sealed class SmsNotificationByIdempotencyKeySpecification : Specification<SmsNotification>
{
    public SmsNotificationByIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}

/// <summary>
/// Delivery follow-ups for an order that the provider has not yet sent — the ones a cancellation must
/// call off. A follow-up is still callable off while it remains scheduled.
/// </summary>
public sealed class PendingFollowUpsByOrderSpecification : Specification<SmsNotification>
{
    public PendingFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n =>
            n.OrderId == orderId &&
            n.Kind == NotificationKind.DeliveryFollowUp &&
            n.ProviderSid != null &&
            n.ProviderStatus == "scheduled");
    }
}

/// <summary>Notifications created (by their own timestamp) within a range, for reconciliation.</summary>
public sealed class SmsNotificationsCreatedBetweenSpecification : Specification<SmsNotification>
{
    public SmsNotificationsCreatedBetweenSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.CreatedAt >= from && n.CreatedAt <= to);
    }
}
