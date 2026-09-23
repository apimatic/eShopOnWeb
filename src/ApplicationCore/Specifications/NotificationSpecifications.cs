using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumbersByOwnerSpecification : Specification<ContactNumber>
{
    public ContactNumbersByOwnerSpecification(string ownerId)
    {
        Query.Where(c => c.OwnerId == ownerId);
    }
}

public class ContactNumberByOwnerAndIdSpecification : Specification<ContactNumber>
{
    public ContactNumberByOwnerAndIdSpecification(string ownerId, int contactNumberId)
    {
        Query.Where(c => c.OwnerId == ownerId && c.Id == contactNumberId);
    }
}

public class ContactNumberByOwnerAndNumberSpecification : Specification<ContactNumber>
{
    public ContactNumberByOwnerAndNumberSpecification(string ownerId, string phoneNumber)
    {
        Query.Where(c => c.OwnerId == ownerId && c.PhoneNumber == phoneNumber);
    }
}

public class OrderNotificationsByOrderSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId);
    }
}

public class NotificationByIdempotencyKeySpecification : Specification<OrderNotification>
{
    public NotificationByIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}

/// <summary>Local notifications whose provider date-sent falls in a window — the same clock the provider is
/// filtered on for reconciliation.</summary>
public class NotificationsByProviderDateSentSpecification : Specification<OrderNotification>
{
    public NotificationsByProviderDateSentSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.ProviderMessageSid != null
            && n.ProviderDateSent != null
            && n.ProviderDateSent >= from
            && n.ProviderDateSent <= to);
    }
}

/// <summary>Not-yet-sent (scheduled) delivery follow-ups for an order, so a cancel can call them off.</summary>
public class ScheduledFollowUpsByOrderSpecification : Specification<OrderNotification>
{
    public ScheduledFollowUpsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.Kind == NotificationKind.DeliveryFollowUp
            && n.ProviderMessageSid != null);
    }
}
