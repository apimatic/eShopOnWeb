using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// All orders that carry a payment, with payment and refunds loaded. Used by reconciliation to
/// line eShop's records up against the provider's by payment reference.
/// </summary>
public class OrdersWithPaymentSpec : Specification<Order>
{
    public OrdersWithPaymentSpec()
    {
        Query
            .Where(o => o.Payment != null)
            .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds);
    }
}
