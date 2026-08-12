using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All notifications owned by a shopper, optionally limited to a set of orders.</summary>
public class NotificationsByBuyerSpecification : Specification<Notification>
{
    public NotificationsByBuyerSpecification(string buyerId)
    {
        Query.Where(n => n.BuyerId == buyerId)
            .OrderBy(n => n.CreatedDate);
    }

    public NotificationsByBuyerSpecification(string buyerId, IEnumerable<int> orderIds)
    {
        Query.Where(n => n.BuyerId == buyerId && orderIds.Contains(n.OrderId))
            .OrderBy(n => n.CreatedDate);
    }
}
