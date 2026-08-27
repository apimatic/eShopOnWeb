using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// All orders that have a payment record — used to line up the provider's transaction
/// report against eShop orders during reconciliation.
/// </summary>
public class OrdersWithPaymentsSpec : Specification<Order>
{
    public OrdersWithPaymentsSpec()
    {
        Query.Where(o => o.Payment != null)
            .Include(o => o.Payment!)
                .ThenInclude(p => p.Refunds);
    }
}
