using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All of one shopper's orders, newest first, with their items and payment state.</summary>
public class CustomerOrdersWithPaymentSpec : Specification<Order>
{
    public CustomerOrdersWithPaymentSpec(string buyerId)
    {
        Query
            .Where(o => o.BuyerId == buyerId)
            .OrderByDescending(o => o.OrderDate);
        Query
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
        Query
            .Include(o => o.Payment!)
                .ThenInclude(p => p.Refunds);
    }
}
