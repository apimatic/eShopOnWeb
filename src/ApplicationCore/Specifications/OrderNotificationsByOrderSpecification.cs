using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Every notification recorded for an order, oldest first.</summary>
public class OrderNotificationsByOrderSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrderSpecification(int orderId)
    {
        Query
            .Where(n => n.OrderId == orderId)
            .OrderBy(n => n.CreatedDate);
    }
}
