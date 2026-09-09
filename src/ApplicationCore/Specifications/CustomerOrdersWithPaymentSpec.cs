using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All of a shopper's orders, newest first, with items and payment state.</summary>
public class CustomerOrdersWithPaymentSpec : Specification<Order>
{
    public CustomerOrdersWithPaymentSpec(string buyerId)
    {
        Query
            .Where(o => o.BuyerId == buyerId)
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered)
            .OrderByDescending(o => o.OrderDate);
    }
}
