using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrdersByBuyerSpec : Specification<Order>
{
    public OrdersByBuyerSpec(string buyerId)
    {
        Query.Where(o => o.BuyerId == buyerId)
             .Include(o => o.OrderItems);
    }
}

public class OrderByIdAndBuyerSpec : Specification<Order>
{
    public OrderByIdAndBuyerSpec(int id, string buyerId)
    {
        Query.Where(o => o.Id == id && o.BuyerId == buyerId)
             .Include(o => o.OrderItems);
    }
}

public class OrderByIdSpec : Specification<Order>
{
    public OrderByIdSpec(int id)
    {
        Query.Where(o => o.Id == id)
             .Include(o => o.OrderItems);
    }
}
