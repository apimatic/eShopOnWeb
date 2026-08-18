using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All notifications raised for a set of orders (used to attach notifications to a shopper's orders).</summary>
public class NotificationsByOrderIdsSpecification : Specification<OrderNotification>
{
    public NotificationsByOrderIdsSpecification(IReadOnlyCollection<int> orderIds)
    {
        Query.Where(n => orderIds.Contains(n.OrderId))
            .AsNoTracking()
            .OrderBy(n => n.Id);
    }
}
