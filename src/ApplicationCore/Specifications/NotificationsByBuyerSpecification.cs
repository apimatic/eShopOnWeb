using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsByBuyerSpecification : Specification<OrderNotification>
{
    public NotificationsByBuyerSpecification(string buyerId)
    {
        Query.Where(n => n.BuyerId == buyerId)
            .OrderBy(n => n.CreatedAt)
            .ThenBy(n => n.Id);
    }
}
