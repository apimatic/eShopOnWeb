using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Notifications for one order, newest first.</summary>
public sealed class OrderNotificationsByOrderSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
            .OrderByDescending(n => n.CreatedAt);
    }
}

/// <summary>All notifications belonging to one shopper.</summary>
public sealed class OrderNotificationsByBuyerSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByBuyerSpecification(string buyerId)
    {
        Query.Where(n => n.BuyerId == buyerId);
    }
}

/// <summary>
/// The scheduled "how did delivery go" follow-ups for an order that are still callable off — accepted by
/// the provider (have a Sid) and not already cancelled.
/// </summary>
public sealed class PendingFeedbackNotificationsByOrderSpecification : Specification<OrderNotification>
{
    public PendingFeedbackNotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.Kind == NotificationKind.DeliveryFeedback
            && n.MessageSid != null
            && !n.ScheduledCanceled);
    }
}

/// <summary>A notification claimed by a resend idempotency key (unique).</summary>
public sealed class OrderNotificationByIdempotencyKeySpecification : Specification<OrderNotification>
{
    public OrderNotificationByIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}

/// <summary>
/// Local messages sent from a given sender that reached the provider (have a Sid), for reconciliation.
/// The date-range narrowing is applied on the provider's send time in the service, on the same clock as
/// the provider-side query.
/// </summary>
public sealed class NotificationsSentFromSenderSpecification : Specification<OrderNotification>
{
    public NotificationsSentFromSenderSpecification(string fromNumber)
    {
        Query.Where(n => n.FromAddress == fromNumber && n.MessageSid != null);
    }
}
