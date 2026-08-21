using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>
/// All orders that have a PayPal payment attached, with their payment state — the eShop side
/// of a reconciliation. Matching to PayPal transactions is done by reference id (the local
/// order id echoed as invoice_id / custom_id), so the whole set is loaded and matched in memory.
/// </summary>
public class OrdersWithPaymentSpecification : Specification<Order>
{
    public OrdersWithPaymentSpecification()
    {
        Query.Where(o => o.Payment != null)
            .Include(o => o.Payment)
                .ThenInclude(p => p!.Refunds);
    }
}
