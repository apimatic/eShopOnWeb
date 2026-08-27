using System;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsByOrderSpec : Specification<OrderNotification>
{
    public NotificationsByOrderSpec(int orderId)
    {
        Query
            .Where(n => n.OrderId == orderId)
            .OrderBy(n => n.CreatedAt);
    }
}

public class NotificationsByBuyerSpec : Specification<OrderNotification>
{
    public NotificationsByBuyerSpec(string buyerId)
    {
        Query
            .Where(n => n.BuyerId == buyerId)
            .OrderBy(n => n.CreatedAt);
    }
}

public class NotificationByIdempotencyKeySpec : Specification<OrderNotification>
{
    public NotificationByIdempotencyKeySpec(string idempotencyKey)
    {
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}

public class NotificationsCreatedInRangeSpec : Specification<OrderNotification>
{
    public NotificationsCreatedInRangeSpec(DateTimeOffset from, DateTimeOffset to)
    {
        Query.Where(n => n.CreatedAt >= from && n.CreatedAt <= to);
    }
}
