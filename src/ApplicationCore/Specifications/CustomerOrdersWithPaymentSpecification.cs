using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>All of a buyer's orders with items and payment state, newest first.</summary>
public class CustomerOrdersWithPaymentSpecification : Specification<Order>
{
    public CustomerOrdersWithPaymentSpecification(string buyerId)
    {
        Query.Where(o => o.BuyerId == buyerId)
            .OrderByDescending(o => o.OrderDate)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
        Query
            .Include(o => o.Payment!)
                .ThenInclude(p => p.Refunds);
    }
}

/// <summary>
/// All orders that carry a payment, across all buyers, with payment state — used by the
/// operator reconciliation report to line eShop records up against PayPal's records.
/// </summary>
public class OrdersWithPaymentSpecification : Specification<Order>
{
    public OrdersWithPaymentSpecification()
    {
        Query.Where(o => o.Payment != null);
        Query
            .Include(o => o.Payment!)
                .ThenInclude(p => p.Refunds);
    }
}
