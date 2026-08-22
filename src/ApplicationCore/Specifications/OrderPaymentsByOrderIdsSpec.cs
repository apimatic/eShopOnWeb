using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderPaymentsByOrderIdsSpec : Specification<OrderPayment>
{
    public OrderPaymentsByOrderIdsSpec(IEnumerable<int> orderIds)
    {
        var ids = orderIds.ToArray();
        Query.Where(p => ids.Contains(p.OrderId));
        Query.Include(p => p.Refunds);
    }
}
