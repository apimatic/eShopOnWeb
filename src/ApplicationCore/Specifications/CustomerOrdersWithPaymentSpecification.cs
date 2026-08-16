using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>The caller's orders with their items and payment state, newest first.</summary>
public class CustomerOrdersWithPaymentSpecification : Specification<Order>
{
    public CustomerOrdersWithPaymentSpecification(string buyerId)
    {
        Query
            .Where(o => o.BuyerId == buyerId)
            .OrderByDescending(o => o.OrderDate)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered)
            .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds);
    }
}
