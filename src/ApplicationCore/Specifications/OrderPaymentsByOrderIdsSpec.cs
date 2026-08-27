using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderPaymentsByOrderIdsSpec : Specification<OrderPayment>
{
    public OrderPaymentsByOrderIdsSpec(IReadOnlyCollection<int> orderIds)
    {
        Query
            .Where(p => orderIds.Contains(p.OrderId))
            .Include(p => p.Refunds);
    }
}
