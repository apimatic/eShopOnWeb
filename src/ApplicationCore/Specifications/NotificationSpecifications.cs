using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>A resend claim by its key, read without tracking (used after a failed insert to read the persisted row).</summary>
public sealed class ResendClaimByKeySpecification : Specification<NotificationResendClaim>
{
    public ResendClaimByKeySpecification(string idempotencyKey) =>
        Query.Where(c => c.IdempotencyKey == idempotencyKey).AsNoTracking();
}

/// <summary>All contact numbers a given shopper has registered.</summary>
public sealed class ContactNumbersByOwnerSpecification : Specification<ContactNumber>
{
    public ContactNumbersByOwnerSpecification(string ownerId) =>
        Query.Where(c => c.OwnerId == ownerId);
}

/// <summary>A single contact number by id, but only if it belongs to the given owner.</summary>
public sealed class ContactNumberByIdForOwnerSpecification : Specification<ContactNumber>
{
    public ContactNumberByIdForOwnerSpecification(int id, string ownerId) =>
        Query.Where(c => c.Id == id && c.OwnerId == ownerId);
}

/// <summary>All notifications for an order (any kind), newest first.</summary>
public sealed class OrderNotificationsByOrderSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrderSpecification(int orderId) =>
        Query.Where(n => n.OrderId == orderId).OrderBy(n => n.CreatedAt);
}

/// <summary>All notifications belonging to a shopper.</summary>
public sealed class OrderNotificationsByOwnerSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOwnerSpecification(string ownerId) =>
        Query.Where(n => n.OwnerId == ownerId).OrderBy(n => n.OrderId).ThenBy(n => n.CreatedAt);
}

/// <summary>Pending, not-yet-sent follow-up notifications for an order (candidates for cancellation).</summary>
public sealed class PendingFollowUpsByOrderSpecification : Specification<OrderNotification>
{
    public PendingFollowUpsByOrderSpecification(int orderId) =>
        Query.Where(n => n.OrderId == orderId
            && n.IsScheduledFollowUp
            && n.ProviderMessageSid != null);
}

/// <summary>
/// Notifications the provider dispatched within a range, filtered on the provider's own
/// <c>date_sent</c> (never a local row-creation column) — the clock reconciliation lines up on.
/// </summary>
public sealed class NotificationsSentInRangeSpecification : Specification<OrderNotification>
{
    public NotificationsSentInRangeSpecification(System.DateTimeOffset from, System.DateTimeOffset to) =>
        Query.Where(n => n.ProviderMessageSid != null
            && n.ProviderDateSent != null
            && n.ProviderDateSent >= from
            && n.ProviderDateSent <= to);
}

/// <summary>
/// Notifications with a provider SID but no recorded <c>date_sent</c> yet, whose local creation is
/// near a reconciliation window — refresh candidates. <c>CreatedAt</c> is used only to bound WHICH
/// rows to refresh (not as the comparison clock), and the set is capped.
/// </summary>
public sealed class NotificationsToRefreshSpecification : Specification<OrderNotification>
{
    public NotificationsToRefreshSpecification(System.DateTimeOffset windowStart, System.DateTimeOffset windowEnd, int cap) =>
        Query.Where(n => n.ProviderMessageSid != null
                && n.ProviderDateSent == null
                && n.CreatedAt >= windowStart
                && n.CreatedAt <= windowEnd)
            .OrderByDescending(n => n.CreatedAt)
            .Take(cap);
}
