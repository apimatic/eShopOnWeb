using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class OrderNotificationsByOrderSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
             .OrderBy(n => n.Id);
    }
}
