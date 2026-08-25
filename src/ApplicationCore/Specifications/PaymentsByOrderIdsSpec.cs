using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaymentsByOrderIdsSpec : Specification<Payment>
{
    public PaymentsByOrderIdsSpec(IReadOnlyList<int> orderIds)
    {
        Query.Where(p => orderIds.Contains(p.EShopOrderId))
             .Include(p => p.Refunds);
    }
}
