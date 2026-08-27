using System;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumbersByBuyerSpecification : Specification<ContactNumber>
{
    public ContactNumbersByBuyerSpecification(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId)
            .OrderByDescending(c => c.CreatedAt);
    }
}

public class ContactNumberByBuyerAndNumberSpecification : Specification<ContactNumber>
{
    public ContactNumberByBuyerAndNumberSpecification(string buyerId, string canonicalNumber)
    {
        Query.Where(c => c.BuyerId == buyerId && c.PhoneNumber == canonicalNumber);
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

public class OrderNotificationByIdempotencyKeySpecification : Specification<OrderNotification>
{
    public OrderNotificationByIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}

public class OrderNotificationsCreatedInRangeSpecification : Specification<OrderNotification>
{
    public OrderNotificationsCreatedInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.CreatedAt >= from && n.CreatedAt <= to);
    }
}

public class PendingFollowUpsForOrderSpecification : Specification<OrderNotification>
{
    public PendingFollowUpsForOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId
            && n.NotificationType == NotificationType.DeliveryFollowUp
            && n.Status == "scheduled"
            && n.ProviderMessageSid != null);
    }
}
