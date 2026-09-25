using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All of a shopper's orders with items and payment state, newest first.</summary>
public class OrdersWithPaymentByBuyerSpec : Specification<Order>
{
    public OrdersWithPaymentByBuyerSpec(string buyerId)
    {
        Query.Where(o => o.BuyerId == buyerId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
        Query.OrderByDescending(o => o.OrderDate);
    }
}
