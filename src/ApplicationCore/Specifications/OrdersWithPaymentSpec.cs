using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Loads every order that has a payment (with capture and refunds), for reconciliation.</summary>
public class OrdersWithPaymentSpec : Specification<Order>
{
    public OrdersWithPaymentSpec()
    {
        Query
            .Where(order => order.Payment != null)
            .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds);
    }
}
