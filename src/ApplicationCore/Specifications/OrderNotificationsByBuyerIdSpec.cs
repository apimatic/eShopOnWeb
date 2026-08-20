using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationsByBuyerIdSpec : Specification<OrderNotification>
{
    public OrderNotificationsByBuyerIdSpec(string buyerId)
    {
        Query.Where(notification => notification.BuyerId == buyerId)
            .OrderBy(notification => notification.CreatedAt);
    }

    public OrderNotificationsByBuyerIdSpec(IEnumerable<int> orderIds)
    {
        var idList = orderIds.ToList();
        Query.Where(notification => idList.Contains(notification.OrderId))
            .OrderBy(notification => notification.CreatedAt);
    }
}
