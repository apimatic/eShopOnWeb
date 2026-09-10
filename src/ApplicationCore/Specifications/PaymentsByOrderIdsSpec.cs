using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaymentsByOrderIdsSpec : Specification<Payment>
{
    public PaymentsByOrderIdsSpec(IEnumerable<int> orderIds)
    {
        var ids = orderIds.ToArray();
        Query
            .Where(p => ids.Contains(p.OrderId))
            .Include(p => p.Refunds);
    }
}
