using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// Every order that carries a PayPal payment, with its payment state and refunds. Backs the
/// operator reconciliation report, which lines all eShop payments up against PayPal's records.
/// </summary>
public class OrdersWithPaymentSpecification : Specification<Order>
{
    public OrdersWithPaymentSpecification()
    {
        Query.Where(o => o.Payment != null);
        Query.Include(o => o.Payment)
            .ThenInclude(p => p!.Refunds);
    }
}
