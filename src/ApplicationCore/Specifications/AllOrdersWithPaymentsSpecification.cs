using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// All orders with their items (and owned payments), used by reconciliation to line eShop's own
/// record up against PayPal's transaction report.
/// </summary>
public class AllOrdersWithPaymentsSpecification : Specification<Order>
{
    public AllOrdersWithPaymentsSpecification()
    {
        Query
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered);
    }
}
