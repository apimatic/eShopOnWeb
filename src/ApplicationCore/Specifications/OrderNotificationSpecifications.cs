using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All notifications sent for one order, oldest first.</summary>
public sealed class OrderNotificationsByOrderSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId).OrderBy(n => n.Id);
    }
}

/// <summary>All notifications belonging to one shopper.</summary>
public sealed class OrderNotificationsByOwnerSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOwnerSpecification(string ownerId)
    {
        Query.Where(n => n.OwnerId == ownerId).OrderBy(n => n.Id);
    }
}

/// <summary>The still-scheduled delivery follow-up for an order, if one exists and has not been called off.</summary>
public sealed class ScheduledFollowUpForOrderSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpForOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
                         && n.IsFollowUp
                         && n.ProviderSid != null
                         && n.DeliveryStatus != "canceled");
    }
}

/// <summary>A notification produced by a resend under a given idempotency key, if any.</summary>
public sealed class OrderNotificationByResendKeySpecification : Specification<OrderNotification>
{
    public OrderNotificationByResendKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.ResendIdempotencyKey == idempotencyKey);
    }
}
