using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All notifications raised for one order, newest first.</summary>
public sealed class NotificationsByOrderSpecification : Specification<Notification>
{
    public NotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId).OrderByDescending(n => n.CreatedAt);
    }
}

/// <summary>All notifications about one shopper's orders.</summary>
public sealed class NotificationsByOwnerSpecification : Specification<Notification>
{
    public NotificationsByOwnerSpecification(string ownerId)
    {
        Query.Where(n => n.OwnerId == ownerId).OrderByDescending(n => n.CreatedAt);
    }
}

/// <summary>The notification produced under a given resend idempotency key, if any.</summary>
public sealed class NotificationByIdempotencyKeySpecification : Specification<Notification>
{
    public NotificationByIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}

/// <summary>Notifications that have a provider message id — the ones reconciliation can line up.</summary>
public sealed class NotificationsWithProviderSidSpecification : Specification<Notification>
{
    public NotificationsWithProviderSidSpecification()
    {
        Query.Where(n => n.ProviderMessageSid != null);
    }
}

/// <summary>The still-scheduled follow-up notifications for an order (candidates to call off on cancel).</summary>
public sealed class ScheduledFollowUpsByOrderSpecification : Specification<Notification>
{
    public ScheduledFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.Kind == NotificationKind.DeliveryFollowUp
            && n.SendState == NotificationSendState.Sent
            && n.ProviderStatus == "scheduled");
    }
}
