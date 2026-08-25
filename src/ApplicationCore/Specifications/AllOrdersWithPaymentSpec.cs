using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class AllOrdersWithPaymentSpec : Specification<Order>
{
    public AllOrdersWithPaymentSpec()
    {
        Query.Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered)
            .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds)
            .OrderByDescending(o => o.OrderDate);
    }
}
