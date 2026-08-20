using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderPaymentsByOrderIdsSpecification : Specification<OrderPayment>
{
    public OrderPaymentsByOrderIdsSpecification(IEnumerable<int> orderIds)
    {
        var ids = orderIds.ToArray();
        Query.Where(p => ids.Contains(p.OrderId))
            .Include(p => p.Refunds);
    }
}
