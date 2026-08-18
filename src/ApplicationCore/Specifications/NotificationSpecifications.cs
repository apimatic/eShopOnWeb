using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Every message sent about a given order, newest first.</summary>
public sealed class NotificationsByOrderSpecification : Specification<Notification>
{
    public NotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
            .OrderBy(n => n.Id);
    }
}

/// <summary>The scheduled delivery follow-up(s) queued for an order (used to call them off on cancel).</summary>
public sealed class ScheduledFollowUpsByOrderSpecification : Specification<Notification>
{
    public ScheduledFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.Kind == NotificationKind.DeliveryFollowUp
            && n.IsScheduled
            && n.ProviderMessageSid != null);
    }
}

/// <summary>A message produced by a prior resend under the same idempotency key.</summary>
public sealed class NotificationByIdempotencyKeySpecification : Specification<Notification>
{
    public NotificationByIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}

/// <summary>Messages the provider accepted (have a provider id) whose send falls within a date range.</summary>
public sealed class NotificationsWithProviderIdInRangeSpecification : Specification<Notification>
{
    public NotificationsWithProviderIdInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.ProviderMessageSid != null
            && n.CreatedAt >= from && n.CreatedAt <= to);
    }
}
