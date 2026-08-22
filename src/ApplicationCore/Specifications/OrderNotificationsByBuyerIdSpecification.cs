using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationsByBuyerIdSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByBuyerIdSpecification(string buyerId)
    {
        Query.Where(n => n.BuyerId == buyerId);
    }
}
