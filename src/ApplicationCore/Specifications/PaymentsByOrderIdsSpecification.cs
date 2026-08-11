using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Loads the payments (with refunds) for a set of order ids, e.g. for the my-orders view.</summary>
public class PaymentsByOrderIdsSpecification : Specification<Payment>
{
    public PaymentsByOrderIdsSpecification(IEnumerable<int> orderIds)
    {
        Query
            .Where(p => orderIds.Contains(p.OrderId))
            .Include(p => p.Refunds);
    }
}
