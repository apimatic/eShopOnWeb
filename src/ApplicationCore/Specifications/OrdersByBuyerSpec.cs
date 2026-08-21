using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All of a buyer's orders, newest first, with payment state.</summary>
public class OrdersByBuyerSpec : Specification<Order>
{
    public OrdersByBuyerSpec(string buyerId)
    {
        Query
            .Where(order => order.BuyerId == buyerId)
            .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered)
            .OrderByDescending(o => o.OrderDate);
    }
}
