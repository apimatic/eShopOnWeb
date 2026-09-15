using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// All orders with their payment state (hold, capture, refunds), used by reconciliation to line
/// eShop's captured orders up against PayPal's transaction record.
/// </summary>
public class OrdersWithPaymentSpecification : Specification<Order>
{
    public OrdersWithPaymentSpecification()
    {
        Query
            .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds);
    }
}
