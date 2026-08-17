using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All notifications for an order, newest first. Optionally scoped to an owning shopper.</summary>
public class OrderNotificationsByOrderSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
            .OrderByDescending(n => n.CreatedAt);
    }

    public OrderNotificationsByOrderSpecification(int orderId, string buyerId)
    {
        Query.Where(n => n.OrderId == orderId && n.BuyerId == buyerId)
            .OrderByDescending(n => n.CreatedAt);
    }
}
