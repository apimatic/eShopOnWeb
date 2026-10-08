using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class CustomerOrdersWithPaymentsSpecification : Specification<Order>
{
    public CustomerOrdersWithPaymentsSpecification(string buyerId)
    {
        Query
            .Where(o => o.BuyerId == buyerId)
            .OrderByDescending(o => o.Id)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered)
            .Include(o => o.Payments)
            .Include(o => o.Refunds)
            .AsNoTracking();
    }
}
