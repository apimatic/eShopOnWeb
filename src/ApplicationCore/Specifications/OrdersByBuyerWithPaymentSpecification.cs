using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class OrdersByBuyerWithPaymentSpecification : Specification<Order>
{
    public OrdersByBuyerWithPaymentSpecification(string buyerId)
    {
        Query
            .Where(o => o.BuyerId == buyerId)
            .Include(o => o.OrderItems)
            .Include(o => o.Payment!)
                .ThenInclude(p => p.Refunds)
            .OrderByDescending(o => o.OrderDate);
    }
}
