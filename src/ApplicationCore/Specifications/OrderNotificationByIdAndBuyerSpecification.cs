using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationByIdAndBuyerSpecification : Specification<OrderNotification>
{
    public OrderNotificationByIdAndBuyerSpecification(int notificationId, string buyerId)
    {
        Query.Where(n => n.Id == notificationId && n.BuyerId == buyerId);
    }
}
