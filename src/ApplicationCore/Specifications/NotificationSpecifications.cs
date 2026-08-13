using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A shopper's registered contact numbers, newest first.</summary>
public sealed class ContactNumbersByOwnerSpecification : Specification<ContactNumber>
{
    public ContactNumbersByOwnerSpecification(string ownerId)
    {
        Query.Where(c => c.OwnerId == ownerId)
            .OrderByDescending(c => c.CreatedDate);
    }
}

/// <summary>All notifications for a given order.</summary>
public sealed class NotificationsByOrderSpecification : Specification<Notification>
{
    public NotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
            .OrderBy(n => n.Id);
    }
}

/// <summary>All notifications belonging to a given shopper.</summary>
public sealed class NotificationsByOwnerSpecification : Specification<Notification>
{
    public NotificationsByOwnerSpecification(string ownerId)
    {
        Query.Where(n => n.OwnerId == ownerId)
            .OrderBy(n => n.Id);
    }
}

/// <summary>
/// Scheduled notifications for an order that have not yet been sent — the follow-ups that must be
/// called off when an order is cancelled.
/// </summary>
public sealed class PendingScheduledNotificationsByOrderSpecification : Specification<Notification>
{
    public PendingScheduledNotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.IsScheduled
            && n.ProviderStatus == "scheduled");
    }
}

/// <summary>
/// Scheduled-and-not-yet-sent notifications aimed at a particular recipient number — used to call off
/// pending messages when a shopper removes that number.
/// </summary>
public sealed class PendingScheduledNotificationsByRecipientSpecification : Specification<Notification>
{
    public PendingScheduledNotificationsByRecipientSpecification(string ownerId, string recipient)
    {
        Query.Where(n => n.OwnerId == ownerId
            && n.Recipient == recipient
            && n.IsScheduled
            && n.ProviderStatus == "scheduled");
    }
}

/// <summary>An existing resend of a notification made under a specific idempotency key, if any.</summary>
public sealed class ResendByIdempotencyKeySpecification : Specification<Notification>
{
    public ResendByIdempotencyKeySpecification(int originalNotificationId, string idempotencyKey)
    {
        Query.Where(n => n.ResendOfNotificationId == originalNotificationId
            && n.IdempotencyKey == idempotencyKey);
    }
}
