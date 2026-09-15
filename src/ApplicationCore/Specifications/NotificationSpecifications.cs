using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All notifications raised for one order, oldest first.</summary>
public sealed class NotificationsByOrderSpecification : Specification<Notification>
{
    public NotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
            .OrderBy(n => n.CreatedAt);
    }
}

/// <summary>All notifications raised for a set of orders (used to decorate a shopper's order list).</summary>
public sealed class NotificationsByOrderIdsSpecification : Specification<Notification>
{
    public NotificationsByOrderIdsSpecification(IEnumerable<int> orderIds)
    {
        var ids = orderIds.ToArray();
        Query.Where(n => ids.Contains(n.OrderId))
            .OrderBy(n => n.CreatedAt);
    }
}

/// <summary>The scheduled delivery-feedback follow-ups for an order that have not yet gone out.</summary>
public sealed class ScheduledFeedbackForOrderSpecification : Specification<Notification>
{
    public ScheduledFeedbackForOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.Kind == NotificationKind.DeliveryFeedback
            && n.State == NotificationState.Scheduled
            && n.ProviderMessageSid != null);
    }
}

/// <summary>Lookup of a prior resend by its caller-supplied idempotency key.</summary>
public sealed class NotificationByIdempotencyKeySpecification : Specification<Notification>
{
    public NotificationByIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}

/// <summary>What eShop believes it sent within a window: messages that carry a provider id and a send time.</summary>
public sealed class SentNotificationsInRangeSpecification : Specification<Notification>
{
    public SentNotificationsInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.ProviderMessageSid != null
            && n.SentAt != null
            && n.SentAt >= from
            && n.SentAt <= to);
    }
}
