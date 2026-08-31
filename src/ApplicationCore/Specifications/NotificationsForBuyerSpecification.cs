using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsForBuyerSpecification : Specification<OrderNotification>
{
    public NotificationsForBuyerSpecification(string buyerId)
    {
        Query.Where(n => n.BuyerId == buyerId)
            .OrderBy(n => n.CreatedAt);
    }
}
