using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationsSpecification : Specification<OrderNotification>
{
    public OrderNotificationsSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
            .OrderBy(n => n.Id);
    }
}

public class OrderNotificationByIdempotencyKeySpecification : Specification<OrderNotification>
{
    public OrderNotificationByIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}

public class ScheduledOrderNotificationsSpecification : Specification<OrderNotification>
{
    public ScheduledOrderNotificationsSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId && n.ProviderStatus == "scheduled" && n.ProviderMessageSid != null);
    }
}

public class OrderNotificationsInRangeSpecification : Specification<OrderNotification>
{
    public OrderNotificationsInRangeSpecification(System.DateTimeOffset from, System.DateTimeOffset to)
    {
        Query.Where(n => n.CreatedAt >= from && n.CreatedAt <= to);
    }
}
