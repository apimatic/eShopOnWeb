using System.Collections.Generic;
using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class PaymentsByBuyerSpec : Specification<Payment>
{
    public PaymentsByBuyerSpec(string buyerId)
    {
        Query
            .Where(p => p.BuyerId == buyerId)
            .Include(p => p.Refunds);
    }
}

public class PaymentsByOrderIdsSpec : Specification<Payment>
{
    public PaymentsByOrderIdsSpec(IEnumerable<int> orderIds)
    {
        Query
            .Where(p => orderIds.Contains(p.OrderId))
            .Include(p => p.Refunds);
    }
}
