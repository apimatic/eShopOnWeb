using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationsByOrderAndBuyerSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrderAndBuyerSpecification(int orderId, string buyerId)
    {
        Query.Where(n => n.OrderId == orderId && n.BuyerId == buyerId)
            .OrderBy(n => n.CreatedAt)
            .ThenBy(n => n.Id);
    }
}
