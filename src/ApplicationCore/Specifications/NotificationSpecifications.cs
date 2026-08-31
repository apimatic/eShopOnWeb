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

public class ContactNumberByIdSpecification : Specification<ContactNumber>
{
    public ContactNumberByIdSpecification(int contactNumberId)
    {
        Query.Where(c => c.Id == contactNumberId);
    }
}

public class OrderNotificationsByOrderSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
            .OrderBy(n => n.CreatedAt);
    }
}

public class OrderNotificationsByBuyerSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByBuyerSpecification(string buyerId)
    {
        Query.Where(n => n.BuyerId == buyerId);
    }
}

public class PendingFollowUpNotificationsSpecification : Specification<OrderNotification>
{
    public PendingFollowUpNotificationsSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.Type == NotificationType.DeliveryFollowUp
            && !n.SendFailed
            && n.ProviderMessageSid != null
            && n.ProviderStatus == "scheduled");
    }
}

public class OrderNotificationByIdempotencyKeySpecification : Specification<OrderNotification>
{
    public OrderNotificationByIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}

public class OrderNotificationsInRangeSpecification : Specification<OrderNotification>
{
    public OrderNotificationsInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.CreatedAt >= from && n.CreatedAt <= to);
    }
}
