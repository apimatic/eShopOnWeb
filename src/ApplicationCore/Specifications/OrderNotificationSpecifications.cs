using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All notifications for one order, newest first.</summary>
public class OrderNotificationsByOrderSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId).OrderBy(n => n.Id);
    }
}

/// <summary>All notifications belonging to one shopper's orders (for GET /api/my-orders enrichment).</summary>
public class OrderNotificationsByBuyerSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByBuyerSpecification(string buyerId)
    {
        Query.Where(n => n.BuyerId == buyerId);
    }
}

/// <summary>A notification already produced under a given resend idempotency key, if any.</summary>
public class OrderNotificationByIdempotencyKeySpecification : Specification<OrderNotification>
{
    public OrderNotificationByIdempotencyKeySpecification(string idempotencyKey)
    {
        Query.Where(n => n.IdempotencyKey == idempotencyKey);
    }
}
