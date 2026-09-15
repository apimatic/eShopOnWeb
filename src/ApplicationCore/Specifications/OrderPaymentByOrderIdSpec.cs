using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderPaymentByOrderIdSpec : Specification<OrderPayment>, ISingleResultSpecification<OrderPayment>
{
    public OrderPaymentByOrderIdSpec(int orderId)
    {
        Query.Where(p => p.OrderId == orderId)
            .Include(p => p.Refunds);
    }
}

public class OrderPaymentsByOrderIdsSpec : Specification<OrderPayment>
{
    public OrderPaymentsByOrderIdsSpec(IEnumerable<int> orderIds)
    {
        var ids = orderIds.ToArray();
        Query.Where(p => ids.Contains(p.OrderId))
            .Include(p => p.Refunds);
    }
}

public class AllOrderPaymentsSpec : Specification<OrderPayment>
{
    public AllOrderPaymentsSpec()
    {
        Query.Include(p => p.Refunds);
    }
}
