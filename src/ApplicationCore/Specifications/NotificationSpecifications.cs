using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Every notification sent for one order, most recent first.</summary>
public class NotificationsByOrderSpecification : Specification<Notification>
{
    public NotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
             .OrderByDescending(n => n.CreatedDate);
    }
}

/// <summary>Every notification for one shopper, used to summarise where each order's messages got to.</summary>
public class NotificationsByBuyerSpecification : Specification<Notification>
{
    public NotificationsByBuyerSpecification(string buyerId)
    {
        Query.Where(n => n.BuyerId == buyerId);
    }
}

/// <summary>A single notification by id (operator scope).</summary>
public class NotificationByIdSpecification : Specification<Notification>
{
    public NotificationByIdSpecification(int notificationId)
    {
        Query.Where(n => n.Id == notificationId);
    }
}

/// <summary>An existing resend keyed by a caller's idempotency key, so a repeat does not send again.</summary>
public class NotificationByIdempotencyKeySpecification : Specification<Notification>
{
    public NotificationByIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}

/// <summary>Delivery follow-ups for an order that are still queued with the provider and not yet sent.</summary>
public class PendingFollowUpsByOrderSpecification : Specification<Notification>
{
    public PendingFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
                         && n.Kind == NotificationKind.DeliveryFollowUp
                         && n.IsScheduled
                         && n.ProviderMessageSid != null);
    }
}

/// <summary>eShop's own record of messages handed to the provider within a date range (for reconciliation).</summary>
public class NotificationsSentInRangeSpecification : Specification<Notification>
{
    public NotificationsSentInRangeSpecification(System.DateTimeOffset from, System.DateTimeOffset to)
    {
        Query.Where(n => n.ProviderMessageSid != null
                         && n.CreatedDate >= from
                         && n.CreatedDate <= to);
    }
}
