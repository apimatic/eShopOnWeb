using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Every order with its payment graph, across all buyers — used by the operator reconciliation
/// report to line eShop's records up against PayPal's.
/// </summary>
public class OrdersWithPaymentSpecification : Specification<Order>
{
    public OrdersWithPaymentSpecification()
    {
        Query
            .Include(o => o.Payment!)
                .ThenInclude(p => p.Refunds);
    }
}
