using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All notifications raised for a given order.</summary>
public class OrderNotificationsByOrderSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
             .OrderBy(n => n.CreatedAt);
    }
}

/// <summary>All notifications belonging to a shopper (across their orders).</summary>
public class OrderNotificationsByBuyerSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByBuyerSpecification(string buyerId)
    {
        Query.Where(n => n.BuyerId == buyerId)
             .OrderBy(n => n.CreatedAt);
    }
}

/// <summary>A single notification by its identifier.</summary>
public class OrderNotificationByIdSpecification : Specification<OrderNotification>, ISingleResultSpecification<OrderNotification>
{
    public OrderNotificationByIdSpecification(int notificationId)
    {
        Query.Where(n => n.Id == notificationId);
    }
}

/// <summary>A notification previously created under a given idempotency key (operator re-sends).</summary>
public class OrderNotificationByIdempotencyKeySpecification : Specification<OrderNotification>, ISingleResultSpecification<OrderNotification>
{
    public OrderNotificationByIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}

/// <summary>Notifications eShop believes it handed to the provider within a date range (for reconciliation).</summary>
public class OrderNotificationsCreatedInRangeSpecification : Specification<OrderNotification>
{
    public OrderNotificationsCreatedInRangeSpecification(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.CreatedAt >= from && n.CreatedAt <= to)
             .OrderBy(n => n.CreatedAt);
    }
}
