using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class OrderWithRefundsSpecification : Specification<Order>
{
    public OrderWithRefundsSpecification(int orderId)
    {
        Query.Where(o => o.Id == orderId)
            .Include(o => o.Refunds);
    }
}

public sealed class OrdersByBuyerSpecification : Specification<Order>
{
    public OrdersByBuyerSpecification(string buyerId)
    {
        Query.Where(o => o.BuyerId == buyerId)
            .Include(o => o.OrderItems)
            .Include(o => o.Refunds)
            .OrderByDescending(o => o.OrderDate);
    }
}

public sealed class AllOrdersWithRefundsSpecification : Specification<Order>
{
    public AllOrdersWithRefundsSpecification()
    {
        Query.Include(o => o.Refunds);
    }
}
