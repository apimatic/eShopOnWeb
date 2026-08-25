using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public sealed class OrdersByBuyerSpec : Specification<Order>
{
    public OrdersByBuyerSpec(string buyerId)
    {
        Query.Where(o => o.BuyerId == buyerId).Include(o => o.OrderItems);
    }
}
