using System;
using System.Collections.Generic;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Notifications raised for a single order, in the order they were created.</summary>
public class SmsNotificationsByOrderSpecification : Specification<SmsNotification>
{
    public SmsNotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
            .OrderBy(n => n.CreatedDate);
    }
}

/// <summary>Notifications raised for a set of orders (for the "my orders" roll-up).</summary>
public class SmsNotificationsByOrdersSpecification : Specification<SmsNotification>
{
    public SmsNotificationsByOrdersSpecification(IEnumerable<int> orderIds)
    {
        var ids = new HashSet<int>(orderIds);
        Query.Where(n => ids.Contains(n.OrderId))
            .OrderBy(n => n.CreatedDate);
    }
}

/// <summary>
/// A pending (not-yet-sent) scheduled follow-up for an order — what must be called off when the
/// order is cancelled so the "how did delivery go?" message never reaches the customer.
/// </summary>
public class PendingFollowUpsByOrderSpecification : Specification<SmsNotification>
{
    public PendingFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n =>
            n.OrderId == orderId &&
            n.IsFollowUp &&
            (n.DeliveryStatus == NotificationDeliveryStatus.Scheduled ||
             n.DeliveryStatus == NotificationDeliveryStatus.Accepted ||
             n.DeliveryStatus == NotificationDeliveryStatus.Queued));
    }
}

/// <summary>An existing re-send performed under a given idempotency key, if any.</summary>
public class SmsNotificationByIdempotencyKeySpecification : Specification<SmsNotification>
{
    public SmsNotificationByIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}

/// <summary>Notifications created within a date range that carry a provider message id (for reconciliation).</summary>
public class SmsNotificationsWithProviderIdBetweenSpecification : Specification<SmsNotification>
{
    public SmsNotificationsWithProviderIdBetweenSpecification(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        Query.Where(n =>
            n.ProviderMessageSid != null &&
            n.CreatedDate >= fromUtc &&
            n.CreatedDate <= toUtc);
    }
}
