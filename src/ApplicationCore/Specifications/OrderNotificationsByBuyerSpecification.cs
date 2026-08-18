using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Every notification belonging to a shopper.</summary>
public class OrderNotificationsByBuyerSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByBuyerSpecification(string buyerId)
    {
        Query
            .Where(n => n.BuyerId == buyerId)
            .OrderBy(n => n.CreatedDate);
    }
}
