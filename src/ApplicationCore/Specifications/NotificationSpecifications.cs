using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ContactNumbersByBuyerSpecification : Specification<ContactNumber>
{
    public ContactNumbersByBuyerSpecification(string buyerId)
    {
        Query.Where(c => c.BuyerId == buyerId).OrderBy(c => c.Id);
    }
}

public class OrderNotificationsByOrderSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId).OrderBy(n => n.Id);
    }
}

public class ScheduledOrderNotificationsSpecification : Specification<OrderNotification>
{
    public ScheduledOrderNotificationsSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId && n.Status == "scheduled" && n.ProviderMessageSid != null);
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
    public OrderNotificationsInRangeSpecification(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        Query.Where(n => n.ProviderMessageSid != null && n.CreatedUtc >= fromUtc && n.CreatedUtc <= toUtc);
    }
}

public class OrdersByBuyerSpecification : Specification<Order>
{
    public OrdersByBuyerSpecification(string buyerId)
    {
        Query.Where(o => o.BuyerId == buyerId)
            .Include(o => o.OrderItems)
            .OrderByDescending(o => o.OrderDate);
    }
}
