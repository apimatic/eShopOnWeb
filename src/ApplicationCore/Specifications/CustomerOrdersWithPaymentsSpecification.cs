using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class CustomerOrdersWithPaymentsSpecification : Specification<Order>
{
    public CustomerOrdersWithPaymentsSpecification(string buyerId)
    {
        Query.Where(o => o.BuyerId == buyerId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered)
            .Include(o => o.PaymentRefunds)
            .OrderByDescending(o => o.OrderDate);
    }
}
