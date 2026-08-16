using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Loads the payments for a set of orders, with their refunds — used to attach payment state to a list of orders.</summary>
public class PaymentsByOrderIdsSpecification : Specification<Payment>
{
    public PaymentsByOrderIdsSpecification(IEnumerable<int> orderIds)
    {
        Query
            .Where(p => orderIds.Contains(p.OrderId))
            .Include(p => p.Refunds);
    }
}
